# ADR 0008：統一本機 LLM 戰局規劃器

## 狀態

Accepted — 2026-08-12。取代 ADR 0007 將 LLM 限定為異常升級層的定位。

2026-08-13：計畫層級與重算生命週期由 ADR 0011 補充；`GamePlan` 不再是每輪整份替換的扁平結果。

2026-08-13：量化動作（`Actions`／`PlannedAction`）機制與其 UI 呈現規則由 ADR 0013 取代並移除；輸入不再包含 `top_hud` 裁切圖，資源數值改以 OCR 文字提供。

## 決策

AgePilot 以同一份 `GamePlan` 作為戰場提示與自動執行的唯一決策來源。自 2026-08-12 起玩家核心升級為 Qwen3-VL：輸入包含完整畫面、原生 HUD／命令面板／小地圖裁切、OCR `GameState`、前一動作與近期事件；模型每輪只輸出一個白名單原子工具、理由與預期畫面結果。

LLM 不得輸出任意鍵盤序列；它只能選擇使用者設定的語意綁定 ID，或從完整畫面輸出單一步驟的 normalized 滑鼠座標。計畫必須通過確定性 schema、期限、信心、綁定 ID 與座標安全邊界驗證。安全 Gate、前景視窗、緊急停止、動作確認及逾時恢復不依賴 LLM。

最初垂直切片只顯示規劃 HUD；其後的 Qwen3-VL 執行切片以 Preview 為預設，只有 Dashboard 授權且每局 Armed 才能消費同一份已驗證 `GamePlan.VisualDecision`，不得另建策略判斷來源。

## Runtime 與小地圖邊界

- 模型為 `models/Qwen3VL-8B-Instruct-Q4_K_M.gguf`，視覺編碼器為 `models/mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf`；llama.cpp 位於 `.runtime/llama.cpp`，HIP 優先、Vulkan 備援，只監聽 loopback。
- `.runtime/` 與 GGUF 不進入版本控制或公開套件。
- Portable 版由 Dashboard 分別選擇 llama.cpp runtime 目錄與 GGUF 模型檔；「測試 LLM」會啟動 backend、等待 health ready，並顯示未設定、啟動中、載入模型、已就緒、規劃中或錯誤狀態。
- HIP 啟動時會尋找 `ROCM_PATH/bin` 或最新的 `Program Files/AMD/ROCm/*/bin`，注入子程序 PATH，並要求 `--list-devices` 實際回報 `ROCmN`；只有 CPU backend 時視為失敗，不得以 health endpoint 冒充 GPU ready。
- 依 ADR 0010，模型契約每輪只允許一個 `Observe`、`Wait`、`LeftClick`、`RightClick` 或 `Drag`；遊戲內不允許鍵盤或語意快捷鍵綁定。輸入送出後，下一輪必須先回報 `Confirmed`、`Failed` 或 `Uncertain`，確認完成前不得執行新動作。
- 量化提示包含動作數量、人口上限、資源村民目標、資源檢查點與重新評估時間。房屋人口容量及配置總數由可靠觀測確定性校正；實際資源村民尚未被視覺確認前，UI 必須標示為「目標配置（非目前實測）」。
- 推論失敗可沿用上一份有效的父層計畫；60 秒期限只使當前視覺動作進入 Minor 重新評估，不得刪除仍有效的 Major／Medium。
- 小地圖第一版只產生水域、森林、開放地形與粗略拓撲；不辨識資源點、敵我單位或地圖名稱。
- 低覆蓋、未探索或跨幀不一致時回傳 Unknown／Unavailable。
