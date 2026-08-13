# AgePilot Privacy

## 預設行為

- 畫面擷取、OCR、GameState 與規則運算全部在本機進行。
- 不上傳 Screenshot、Frame buffer、玩家名稱或對局資料。
- 不需要 AgePilot 帳號。
- Telemetry 預設不存在／關閉。

## 本機資料

```text
%LocalAppData%\AgePilot\settings.json
%LocalAppData%\AgePilot\agepilot.db
%LocalAppData%\AgePilot\logs\agepilot.jsonl
```

`settings.json` 保存 HUD Profile、透明度、掃描間隔、是否保存 Session，以及使用者設定的自動操作熱鍵與按鍵序列。

`agepilot.db` 保存：

- Session 開始／結束時間。
- 每五秒的可信資源與人口 Snapshot。
- Rule 首次變成 active 時的 Recommendation event。

資料庫不保存圖片。刪除 `%LocalAppData%\AgePilot` 可移除全部 AgePilot 本機資料。

使用者可在 Dashboard 關閉 Session 紀錄。診斷 JSON 只在使用者主動按下匯出並選擇路徑後建立，包含程式／系統版本、非敏感設定、辨識健康狀態與最近 Session 摘要；不包含 Screenshot、按鍵內容、玩家帳號或自動上傳行為。

本機診斷事件也可在 Dashboard 關閉。一般 `agepilot.jsonl` 單檔上限 5 MB 並保留一份輪替檔，可記錄 OCR 原始文字、信心、解析結果、拒絕原因與自動操作事件，但不記錄 Frame buffer 或遊戲畫面。AgePilot 不建立 `traces` 目錄，也不保存動作前後截圖。

## 未來變更

若未來加入 Telemetry 或雲端功能，必須明確 Opt-in，並在啟用前更新本文件與 UI 說明。
