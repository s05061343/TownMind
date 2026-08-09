# AgePilot

> A calm companion for Age of Empires II.

AgePilot 是 Windows 本機桌面教練，從《Age of Empires II: Definitive Edition》畫面讀取 HUD，提供低干擾的經濟與人口建議。它不注入遊戲、不讀取遊戲記憶體、不發送輸入，也不預設上傳任何畫面。

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
- 本機 Vision／Rule diagnostics，以及使用者主動觸發的匿名 JSON 匯出。
- 可重跑的 Screenshot Vision、Replay 與 Live performance JSON 報告。

## Privacy

AgePilot 不保存或上傳遊戲 Screenshot。詳見 [Privacy](doc/PRIVACY.md)。

## Disclaimer

AgePilot 是非官方工具，與 Microsoft、Xbox Game Studios、World's Edge 或 Forgotten Empires 無關。使用公開版本前，使用者仍應確認適用的遊戲服務條款與政策。

公開條款未明文確認只讀 OCR Overlay 是否獲允許。在取得官方 Support 明確答覆前，建議只用於單人、對 AI 與非排名測試；詳見 [政策邊界檢查](doc/business/policy-review-2026-08-09.md)。

## License

本 repository 的自有程式碼授權尚未指定。第三方元件見 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
