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
- [x] AOE2 執行中的全螢幕 Capture 實機驗證。
- [ ] 多樣本 Vision Gate 準確率驗收。

目前狀態是 **Playable Prototype 已通過首次實機串接**，不是 Public Alpha。GDI 已在目前校正環境取得正確畫面；其他顯示模式仍需個別驗證。

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

## 7. 尚未完成

- PaddleOCR 已選定；仍需完成實際 ROI 整合與 Vision Gate 測量。
- `ocr-image` 已提供固定截圖的 ROI OCR；仍需累積樣本與進行 Vision Gate 測量。
- `scan-live` 會尋找 AOE2、擷取單幀並直接輸出五個 HUD OCR 結果。
- `overlay` 以 2 FPS 執行持續擷取與 OCR，經 Temporal GameState 後顯示 Farmer Mode 建議。
- Windows Graphics Capture 實際串接。
- HUD Anchor 圖示辨識。
- 多張遊戲樣本與 metadata ground truth。
- Vision Gate 準確率測量。

以上項目未完成前，不宣稱 OCR 或任意解析度正式支援。

## 8. 下一批樣本需求

為了建立可用資料集，需要同一環境下至少涵蓋：

- 四個時代。
- 各資源的一位數、兩位數、三位數及四位數。
- 人口未滿、接近上限及卡人口。
- 暫停、選單、Alt+Tab 回復及對局結束。

每張遊戲截圖必須附人工標註 metadata。

## 9. 第一筆 Ground Truth

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
