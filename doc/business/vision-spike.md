# Phase 0 — Vision Spike 業務規格

## 實作狀態（2026-08-09）

- [x] AOE2 視窗定位。
- [x] GDI 單幀擷取 backend。
- [x] 標準化 ROI 與 2560×1440 Profile。
- [x] 本機 PaddleOCR 與真實截圖回歸。
- [x] 2 FPS 持續監測迴圈。
- [x] Confidence fail-closed 與 Temporal median state。
- [x] PopulationCritical、PopulationLow、WoodOverflow Farmer Rules。
- [x] Priority、Cooldown 與最多三條建議。
- [x] WPF Compact Overlay 與背景取消。
- [x] Dashboard、JSON 設定與 Overlay UI 啟停。
- [x] SQLite Session、五秒 Snapshot、Recommendation events 與最近對局檢視。
- [x] Chinese V5 時代 OCR（黑暗／封建／城堡／帝王）。
- [x] Farmer Rules：人口低、人口滿、木材偏高、升城採金、可升城、可升帝王、資源嚴重溢出。
- [x] Active recommendation、次要建議與 Dismiss-until-resolved lifecycle。
- [x] AOE2 執行中的全螢幕 Capture 實機驗證。
- [x] 四張 Screenshot manifest 與自動 OCR ground-truth 回歸（正常、Overlay、零值、暫停）。
- [x] Self-contained win-x64 Portable package、SHA-256 與診斷匯出。
- [x] `GameNotFound / Detected / Loading / Active / Paused / Unavailable / Ended` 狀態與暫停 fail-closed。
- [x] 變更 ROI 快取、零拷貝 Frame bridge 與可重跑 Vision／Replay JSON 報告。
- [ ] 多樣本 Vision Gate 準確率驗收。
- [ ] 小地圖地形／拓撲 MapContext 多樣本 Gate；未通過前不得驅動自動操作。

目前狀態是 **Public Alpha 軟體功能已完成，Vision／實機 Gate 仍在累積證據**。GDI 已在目前校正環境取得正確畫面；其他顯示模式仍需個別驗證。

## 1. 目的

Phase 0 只回答一個問題：AgePilot 能否從 AOE2 DE 畫面可靠取得 Tier A 資料，並在資料不可靠時保持沉默？

本階段不是公開產品，不包含正式教練 Overlay、SQLite、完整 Dashboard 或進階策略。

## 2. 已確認校正環境

| 項目 | 基準值 |
|---|---|
| 解析度 | 2560×1440 |
| 顯示模式 | 全螢幕 |
| 遊戲語言 | 繁體中文 |
| HUD 縮放 | 50% |
| HUD 位置 | 資源列位於左上角 |
| 校正圖 | `doc/Snipaste_2026-08-09_16-29-15.jpg` |

右上角可存在效能監控 Overlay；Phase 0 的 ROI 不得與其重疊。

## 3. 動態對應的定義

第一步的「動態對應」是：

- ROI 儲存為相對於校正畫面的 0～1 標準化座標。
- 輸入畫面大小改變時，依寬高分別換算像素矩形。
- 矩形必須被限制在輸入畫面範圍內。

這不代表任意 HUD Scale、HUD Layout 或語言已受支援。當 HUD 元素不是等比例移動時，必須使用後續 Anchor 校正；找不到 Anchor 時回報 `CalibrationRequired`，不得使用猜測座標。

## 4. Phase 0 Tier A 欄位

第一批 ROI：

- Wood
- Food
- Gold
- Stone
- Population

時代與遊戲時間等取得可靠樣本後再加入。

小地圖屬於 Tier C 策略上下文。現行校正 Profile 已加入標準化 Minimap ROI，第一版只分析水域、森林、開放地形與粗略拓撲，且必須跨三幀確認；未知與戰爭迷霧不得解讀為沒有資源。

## 5. 資料契約

每個讀值必須包含：

- 原始值或無值。
- Confidence。
- 觀察時間。
- `Raw / Confirmed / Rejected / Stale / Unavailable` 狀態。

未知值不得以 0 表示。低信心、過期或矛盾資料不得進入建議規則。

## 6. 當前交付範圍

- .NET solution 與 Core / Vision / App / Tests 骨架。
- Profile JSON 載入及驗證。
- 標準化 ROI 至像素 ROI 的換算。
- 參考圖片資訊讀取與 ROI 列表輸出。
- AOE2 視窗尋找介面與 Windows 實作。
- Capture 與 OCR 抽象介面。
- Windows GDI 單幀擷取 Spike 與 BMP 輸出；此實作只用於驗證，正式 Capture backend 仍需比較 Windows Graphics Capture。
- ROI、Profile 與數字解析的自動測試。

## 7. 尚未完成的驗收證據

- Windows Graphics Capture 實際串接。
- HUD Anchor 圖示辨識。
- 封建、城堡、帝王及更多資源位數的 ground truth。
- Vision Gate 準確率測量。
- 五局完整實機無 Crash、錯誤提示與 FPS 影響紀錄。

以上項目未完成前，不宣稱 OCR 或任意解析度正式支援。

## 8. 下一批樣本需求

為了建立可用資料集，需要同一環境下至少涵蓋：

- 四個時代。
- 各資源的一位數、兩位數、三位數及四位數。
- 人口未滿、接近上限及卡人口。
- 暫停、選單、Alt+Tab 回復及對局結束。

每張遊戲截圖必須附人工標註 metadata。

## 9. Ground Truth 與目前基準

`Snipaste_2026-08-09_16-29-15.jpg` 的人工標註為：

```text
Wood        200
Food        200
Gold        100
Stone       200
Population  4 / 5
```

機器可讀版本位於 `testdata/screenshots/manifest.json`。這筆資料目前只能驗證 ROI、圖片尺寸與後續 OCR 管線整合，單張樣本不能用來宣稱準確率達標。

目前回歸結果：五個欄位與本筆 ground truth 完全一致；Population confidence 約 80%，其他資源約 99.9%～100%。

第二筆實機畫面確認 Food `0` 的 OCR confidence 約 50.5%。45%～70% 的候選值必須連續兩幀完全相同才確認，原始 confidence 保留；低於 70% 的資料不得驅動高信心 Coach Rule。

Cooldown 只適用於未來的聲音、Toast 或重新通知，不得隱藏仍然成立的 Overlay active recommendation。

Dismiss 只在同一段 active condition 期間隱藏建議；條件解除後清除 Dismiss 狀態，未來再次成立時重新提示。SQLite RecommendationEvent 在 inactive → active 時寫入一次，不隨畫面更新重複寫入。

截至 2026-08-09，manifest 有四張 2560×1440／全螢幕／繁中／HUD 50% 黑暗時代樣本，其中包含正常遊戲、Overlay、食物為零與主選單暫停畫面。四張的六個必要 HUD 欄位共 24 個 ground-truth 值全部完全相符：

```text
FieldExactAccuracy  100% (24/24)
FrameExactAccuracy  100% (4/4)
HighConfidenceErrorRate  0% (0/N)
FalseRecommendationRate  0%
RecommendationExactRate  100% (4/4)
```

這是小樣本基準，不足以宣稱第 10.3 節統計 Gate 通過；回歸測試會確保後續變更不得破壞這四張已知案例。

200 幀最大吞吐 Replay 為 200/200、0 failure，50 個暫停幀全部停止建議。此壓力模式的 OCR 平均／p95 為 571.6／666.8 ms、CPU 48.07%、Peak Working Set 508.3 MB；因為它不模擬遊戲中的等待週期且同時常駐四張完整 BGRA 測試影格，只作為最壞吞吐與穩定性證據，不能代替實機效能 Gate。完整紀錄見 `business/verification-2026-08-09.md`。
