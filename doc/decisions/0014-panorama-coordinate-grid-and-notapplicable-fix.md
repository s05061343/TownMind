# ADR 0014：全景圖／小地圖疊加座標網格，並修正 NotApplicable 誤判

## 狀態

Accepted — 2026-08-13

## 背景

實機測試（05:37-05:40 UTC session）確認自動操作主線已經打通——滑鼠點擊真的送達遊戲、人口與時代也確實變化——但觀察到兩個讓自動操作撐不久就停用的問題：

1. `AutomationController.Handle` 只顯式處理 `PreviousActionResult` 的 `Confirmed`/`Failed`/`Uncertain` 三種值。VLM 有時該回報這三者之一卻回報 `NotApplicable`（甚至在其自身文字描述已經確認動作成功的情況下），程式碼會落入通用的「等待確認」分支，空等到 30 秒 deadline 才當作一次逾時失敗處理。
2. 世界座標（`VisualCoordinateSpace.Panorama`）完全依賴 VLM 自己生成連續 normalized X/Y，準確度不足；log 中觀察到模型連續多輪點擊同一個猜測座標卻沒有準確落在目標物件上。對照命令面板（`CommandGrid`）用離散 row/column 定位就穩定得多。

## 決策

`AutomationController.Handle` 判斷前一動作結果時，把 `NotApplicable` 併入 `Uncertain` 一起處理（`is PreviousActionResult.Uncertain or PreviousActionResult.NotApplicable`），一律 fail-closed、立即清空 pending 狀態，不再空等 deadline。`LlamaServerPlanner.cs` 的 system prompt 同步補充：有 `previousAction` 時 `previousActionResult` 不可以是 `NotApplicable`。

`VisualPromptImageEncoder.Encode` 在 `panorama`（固定 1536×864）與 `minimap` 裁切圖上，JPEG 編碼前疊加一層半透明座標網格（panorama 12(A-L)×8(1-8)、minimap 6(A-F)×6(1-6)，欄字母列數字、由左上角起算），供 VLM 參照估算座標。這是**視覺輔助，不是新的定位協議**：`VisualToolAction` 的 `X`/`Y` schema 不變，仍是連續 normalized 浮點數；`GamePlanValidator.CoordinatesAreSafe`、`AutomationController.Safe`、`MouseCoordinateMapper.TryResolve` 均不需改動。`command_panel` 裁切圖不套用此網格，因為它已經有自己的離散 `CommandGridRegion`／row/column 定位機制。System prompt 同步說明網格慣例與座標換算範例。

## 後果

- 前一動作結果不明確時，系統能更快進入下一輪判斷，不再被 `NotApplicable` 卡住浪費 30 秒。
- Panorama／Minimap 點擊準確度是否明顯改善，仍需實機驗證（`agepilot.jsonl` 的 `automation.sent`／`automation.blocked` 座標與遊戲畫面比對）；若效果不足，下一步才考慮更重的方案（例如物件偵測輔助定位）。
- 圖片 token 成本不受影響：`--image-min-tokens`／`--image-max-tokens` 依解析度而非內容複雜度計算，畫格線不增加 vision token 數。
