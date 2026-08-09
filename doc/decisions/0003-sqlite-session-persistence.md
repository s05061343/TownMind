# ADR-0003：使用 SQLite 保存本機對局 Session

- **狀態：** Accepted
- **日期：** 2026-08-09

## 決策

- 使用 `Microsoft.Data.Sqlite` 8.0.29。
- DB 位於 `%LocalAppData%\AgePilot\agepilot.db`。
- 啟用 WAL 與 foreign keys。
- 遊戲視窗首次連線時建立 Session，離線或 Overlay 關閉時結束。
- 每五秒保存一筆可信 GameState Snapshot。
- Recommendation 只在 Rule 從 inactive 轉成 active 時記錄一次。
- 不保存 Screenshot、Frame buffer、玩家名稱或遊戲畫面。

## Schema

```text
GameSessions
GameSnapshots
RecommendationEvents
```

所有 SQL 值使用參數化命令。Infrastructure 實作 `ISessionRepository`，Core 與 Vision 不依賴 SQLite。

## 理由

- 不需外部服務，符合 Local 與 Privacy 原則。
- 五秒 Snapshot 足以產生資源趨勢，同時避免 2 FPS 寫入壓力。
- WAL 讓 Dashboard 讀取歷史時不需停止監測。
