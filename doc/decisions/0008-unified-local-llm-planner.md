# ADR 0008：統一本機 LLM 戰局規劃器

## 狀態

Accepted — 2026-08-12。取代 ADR 0007 將 LLM 限定為異常升級層的定位。

## 決策

AgePilot 以同一份 `GamePlan` 作為戰場提示與自動執行的唯一決策來源。自 2026-08-12 起玩家核心升級為 Qwen3-VL：輸入包含完整畫面、原生 HUD／命令面板／小地圖裁切、OCR `GameState`、前一動作與近期事件；模型每輪只輸出一個白名單原子工具、理由與預期畫面結果。

LLM 不得輸出鍵盤序列、滑鼠座標或逐步控制事件。計畫必須通過確定性 schema、期限、信心與條件白名單驗證。安全 Gate、前景視窗、緊急停止、動作確認及逾時恢復不依賴 LLM。

最初垂直切片只顯示規劃 HUD；其後的 Qwen3-VL 執行切片以 Preview 為預設，只有 Dashboard 授權且每局 Armed 才能消費同一份已驗證 `GamePlan.VisualDecision`，不得另建策略判斷來源。

## Runtime 與小地圖邊界

- 模型為 `models/Qwen3VL-8B-Instruct-Q4_K_M.gguf`，視覺編碼器為 `models/mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf`；llama.cpp 位於 `.runtime/llama.cpp`，HIP 優先、Vulkan 備援，只監聽 loopback。
- `.runtime/` 與 GGUF 不進入版本控制或公開套件。
- Portable 版由 Dashboard 分別選擇 llama.cpp runtime 目錄與 GGUF 模型檔；「測試 LLM」會啟動 backend、等待 health ready，並顯示未設定、啟動中、載入模型、已就緒、規劃中或錯誤狀態。
- HIP 啟動時會尋找 `ROCM_PATH/bin` 或最新的 `Program Files/AMD/ROCm/*/bin`，注入子程序 PATH，並要求 `--list-devices` 實際回報 `ROCmN`；只有 CPU backend 時視為失敗，不得以 health endpoint 冒充 GPU ready。
- 第一版模型契約使用 llama.cpp strict JSON schema，只要求策略、目標、原因、信心與一個白名單下一步；執行前置與完成條件由確定性程式持有。
- 量化提示包含動作數量、人口上限、資源村民目標、資源檢查點與重新評估時間。房屋人口容量及配置總數由可靠觀測確定性校正；實際資源村民尚未被視覺確認前，UI 必須標示為「目標配置（非目前實測）」。
- 推論失敗可沿用上一份計畫，但建立 60 秒後強制失效。
- 小地圖第一版只產生水域、森林、開放地形與粗略拓撲；不辨識資源點、敵我單位或地圖名稱。
- 低覆蓋、未探索或跨幀不一致時回傳 Unknown／Unavailable。
