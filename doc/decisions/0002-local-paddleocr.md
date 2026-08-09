# ADR-0002：使用本機 PaddleOCR 作為 Phase 0 OCR 引擎

- **狀態：** Accepted for Spike
- **日期：** 2026-08-09

## 背景

本機沒有 Tesseract、ImageMagick、Python OCR 或可直接使用的 Windows OCR 開發環境。Phase 0 必須讓使用者能從實際遊戲截圖取得數字結果，不能只交付 OCR 介面。

## 決策

- 使用 `Sdcb.PaddleOCR` 3.3.1 系列套件。
- 模型使用 `Sdcb.PaddleOCR.Models.Local`，不在執行時下載模型。
- 顯式固定 `Sdcb.PaddleOCR.Models.LocalV5` 3.3.1，避免 NuGet 選到缺少新版內嵌模型的 3.0.0。
- Windows x64 推論使用 `Sdcb.PaddleInference.runtime.win64.mkl` 3.3.1.70。
- 圖像解碼、裁切及縮放使用 OpenCvSharp Windows runtime。
- OCR 僅處理已校正的小型 ROI，輸出仍經過數字 parser、confidence 與 validation。

## 理由

- 模型與 runtime 可由 NuGet 還原，使用者不需另裝 Python 或 OCR 執行檔。
- 完全在本機執行，符合隱私與 Local 原則。
- OCR 仍位於 `IOcrEngine` 後方，後續可用 benchmark 替換。

## 代價與限制

- NuGet 還原與發佈體積會顯著增加。
- 首次模型初始化比純規則或模板辨識慢。
- 第三方套件與模型授權必須在公開發佈前彙整。
- 本決策只代表 Phase 0 可用性，是否進入 Alpha 仍由 Vision Gate 數據決定。
