# AgePilot

<p align="center">
  <img src="assets/branding/agepilot-logo-256.png" width="160" alt="AgePilot 老鷹六角徽章" />
</p>

> A calm companion for Age of Empires II.

AgePilot 是 Windows 本機桌面教練，從《Age of Empires II: Definitive Edition》畫面讀取 HUD，提供低干擾的經濟與人口建議，並可由使用者選擇啟用純滑鼠視覺操作。它不注入遊戲、不讀取遊戲記憶體，也不預設上傳任何畫面。

## Public Alpha 支援環境

- Windows x64
- AOE2 DE 繁體中文
- 2560×1440 全螢幕
- HUD Scale 50%

其他解析度可透過標準化 ROI 等比例換算，但在加入對應測試樣本前不視為正式支援。

## 執行

一般使用者可解壓 `AgePilot-public-alpha-win-x64.zip`，直接雙擊 `AgePilot.App.exe`；不需要另外安裝 .NET。

開發模式：

```powershell
dotnet restore AgePilot.sln
dotnet build AgePilot.sln --no-restore
dotnet run --no-build --project src\AgePilot.App
```

Dashboard 可啟動 Overlay、保存設定並查看最近 Session。詳細說明見 [執行指南](doc/guides/running-phase0.md)。

## 主要功能

- 本機 PaddleOCR 資源、人口與時代辨識。
- Confidence 與 Temporal confirmation。
- Farmer Mode 規則與可略過建議。
- Compact Overlay、滑鼠穿透與全域快捷鍵。
- Dashboard、JSON 設定與可停用的 SQLite Session history。
- System Tray 常駐；關閉／最小化 Dashboard 或啟動 Overlay 時自動收至通知區。
- 可選純滑鼠經濟操作；只在 AOE2 位於前景且畫面可靠時移動游標並透過 Windows SendInput 發送滑鼠事件，每一步都等待新畫面確認。
- 實驗性通用經濟代理：可見資源／空地分析、閒置村民分配、補房、21 人升封建、前置建築與升城確認流程。
- 本機 Vision／Rule diagnostics，以及使用者主動觸發的匿名 JSON 匯出。
- 可重跑的 Screenshot Vision、Replay 與 Live performance JSON 報告。

## Privacy

AgePilot 不保存或上傳遊戲 Screenshot。詳見 [Privacy](doc/PRIVACY.md)。

## Disclaimer

AgePilot 是非官方工具，與 Microsoft、Xbox Game Studios、World's Edge 或 Forgotten Empires 無關。使用公開版本前，使用者仍應確認適用的遊戲服務條款與政策。

公開條款未明文確認只讀 OCR Overlay 或外部自動操作是否獲允許；是否使用及使用情境由專案負責人判斷，詳見 [政策邊界檢查](doc/business/policy-review-2026-08-09.md)。

## License

本 repository 的自有程式碼授權尚未指定。第三方元件見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
