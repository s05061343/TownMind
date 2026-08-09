# Public Alpha 交付與驗收紀錄

## 軟體交付狀態（2026-08-09）

- [x] 本機持續 Capture、OCR、Temporal GameState 與 fail-closed。
- [x] Farmer Mode 確定性規則、priority、dismiss-until-resolved。
- [x] Compact Overlay、拖曳、關閉、滑鼠穿透與快捷鍵。
- [x] System Tray 常駐、關閉／最小化隱藏、Overlay 啟動後自動收合及 Tray-only Exit。
- [x] Dashboard 設定、遊戲連線、Live diagnostics 與最近對局。
- [x] 可停用的 SQLite Session／Snapshot／Recommendation event。
- [x] 使用者主動 JSON 診斷匯出；不含 Screenshot 或帳號資訊。
- [x] Privacy、Disclaimer、第三方授權與執行指南。
- [x] Self-contained `win-x64` Portable ZIP 與 SHA-256。
- [x] Debug／Release build、28 項自動測試與發佈後 OCR smoke test。
- [x] 明確遊戲生命週期、主選單暫停辨識與暫停建議抑制。
- [x] 可重跑 Vision／Replay JSON 報告與 200 幀無 Crash 壓力回放。
- [x] 可停用、會輪替的結構化本機 JSONL 診斷事件。

## 產物

```text
artifacts/packages/AgePilot-public-alpha-win-x64.zip
artifacts/packages/AgePilot-public-alpha-win-x64.zip.sha256
```

目前產物 SHA-256：`159BA5477F58BB004B621B9AE8EAA1EAEF5569D1466F0A31CEFF9FC64BF6F664`。

建置命令：

```powershell
.\scripts\package.ps1
```

## 尚待人工 Gate

下列項目需要真實遊戲時間或全新 Windows 測試機，不能由單元測試代替：

- [ ] 封建、城堡、帝王時代與不同位數截圖，使 Vision 樣本具有代表性。
- [ ] 五局完整實機測試不 Crash，記錄錯誤提示與洗版事件。
- [ ] AOE2 FPS 影響、AgePilot CPU／記憶體長時間量測。
- [ ] 全新 Windows x64 解壓、啟動與 Overlay 串接驗證。
- [x] 2026-08-09 已查核公開官方條款並記錄風險邊界。
- [ ] 正式公開前向 Age of Empires Support 提交功能描述並保存明確答覆。

完成這些人工 Gate 前，版本可供目前校正環境內部測試，但不得對外宣稱 Vision Gate 或 Public Alpha 驗收已完全通過。
