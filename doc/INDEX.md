# AgePilot 文件索引

本檔案是專案文件記憶的唯一入口。實作時先依任務選讀相關文件，不需要一次載入全部內容。

## 執行基準

| 文件 | 用途 | 何時讀取 |
|---|---|---|
| [AgePilot_Project_Plan_v2.md](AgePilot_Project_Plan_v2.md) | 目前產品範圍、Gate、里程碑與驗收基準 | 規劃功能、調整階段或驗收時 |
| [business/vision-spike.md](business/vision-spike.md) | Phase 0 業務規則、支援矩陣與完成定義 | Capture、ROI、OCR、Observation 工作 |
| [decisions/0001-normalized-roi.md](decisions/0001-normalized-roi.md) | 標準化 ROI 與 Anchor 校正的架構決策 | 修改 HUD 定位或解析度支援時 |

## 參考資料

| 文件 | 用途 |
|---|---|
| [AgePilot_Project_Plan.md](AgePilot_Project_Plan.md) | 初始完整構想；僅供歷史與長期願景參考 |
| [Snipaste_2026-08-09_16-28-36.jpg](Snipaste_2026-08-09_16-28-36.jpg) | 2560×1440、全螢幕、繁中、HUD 50% 設定證據 |
| [Snipaste_2026-08-09_16-29-15.jpg](Snipaste_2026-08-09_16-29-15.jpg) | Phase 0 遊戲 HUD 校正參考圖 |
| [../testdata/screenshots/manifest.json](../testdata/screenshots/manifest.json) | 截圖樣本、環境與人工標註 ground truth |

## 文件維護規則

- 業務行為與程式碼必須在同一次變更中同步。
- 新增文件後必須加入本索引。
- 架構取捨使用 `doc/decisions/NNNN-title.md` 記錄。
- 支援環境、驗收數據與已知限制必須有證據，不可只寫「支援」。
