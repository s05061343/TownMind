# AgePilot 文件索引

本檔案是專案文件記憶的唯一入口。實作時先依任務選讀相關文件，不需要一次載入全部內容。

## 執行基準

| 文件 | 用途 | 何時讀取 |
|---|---|---|
| [AgePilot_Project_Plan_v2.md](AgePilot_Project_Plan_v2.md) | 目前產品範圍、Gate、里程碑與驗收基準 | 規劃功能、調整階段或驗收時 |
| [PRIVACY.md](PRIVACY.md) | 本機資料、無上傳與刪除方式 | 資料保存、Telemetry 或公開發佈時 |
| [business/vision-spike.md](business/vision-spike.md) | Phase 0 業務規則、支援矩陣與完成定義 | Capture、ROI、OCR、Observation 工作 |
| [business/public-alpha-release.md](business/public-alpha-release.md) | Public Alpha 軟體交付、產物與人工 Gate | 發佈前驗收、封裝與剩餘實機證據 |
| [business/verification-2026-08-09.md](business/verification-2026-08-09.md) | 自動測試、Vision／Replay 數據與未通過項目 | 評估 Gate 證據或比較效能回歸時 |
| [business/policy-review-2026-08-09.md](business/policy-review-2026-08-09.md) | 官方政策來源、只讀技術邊界與發佈限制 | 公開發佈、功能邊界或政策複查時 |
| [decisions/0001-normalized-roi.md](decisions/0001-normalized-roi.md) | 標準化 ROI 與 Anchor 校正的架構決策 | 修改 HUD 定位或解析度支援時 |
| [decisions/0002-local-paddleocr.md](decisions/0002-local-paddleocr.md) | 本機 PaddleOCR runtime、模型與替換邊界 | OCR 引擎、套件或部署工作 |
| [decisions/0003-sqlite-session-persistence.md](decisions/0003-sqlite-session-persistence.md) | Session、Snapshot、Recommendation event 與隱私邊界 | 歷史資料、報告或 DB schema 工作 |
| [guides/running-phase0.md](guides/running-phase0.md) | 建置、參考圖 OCR、即時單幀掃描與已知限制 | 執行或驗證目前程式時 |

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
