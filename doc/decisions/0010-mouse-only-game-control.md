# ADR 0010：遊戲控制僅使用滑鼠

## 狀態

Accepted — 2026-08-12

2026-08-13：ADR 0015 修訂本決策的純滑鼠限制，允許對遊戲送出快捷鍵。本文其餘安全機制（滑鼠實機測試、視窗 Handle 綁定、前景檢查、游標讀回、fail closed）全部維持有效。

## 決策

AgePilot 不向 AOE2 發送任何鍵盤快捷鍵。遊戲內操作只允許滑鼠左鍵、右鍵與拖曳；AgePilot 自身的全域啟用、緊急停止與 Overlay 熱鍵不受影響。

世界中的村民、建築、資源與地面由 VLM 從當前 panorama 辨識。左下命令面板使用 HUD profile 的 `CommandGridRegion`、列數與欄數映射格位中心。每個輸入動作必須帶有可稽核目標，送出後進入 pending，下一幀確認前不得送出新動作；不確定、逾時、矛盾或低信心一律 fail closed。

每次 AgePilot 程序啟動後，使用者必須先在 Dashboard 執行獨立滑鼠測試。測試只移動游標、不點擊：保存原座標、移動至 AOE2 安全區、以 `GetCursorPos` 讀回、移回原座標並再次讀回。測試成功只對目前程序及目前 AOE2 視窗 Handle 有效。正式點擊或拖曳前也必須讀回游標位置；位置不符時不得呼叫 `SendInput`。

GUI 必須安裝 WPF Dispatcher、AppDomain 與未觀察 Task 的全域例外處理。診斷事件須包含 PID 與執行緒 ID；例外須保留完整 stack trace、inner exception、實際執行檔及命令列。主要 JSONL 無法寫入時，改寫同目錄的 `agepilot-emergency.log`，不得靜默吞掉診斷失敗。

目前只校準 2560×1440、全螢幕、繁中 UI、HUD 50%。沒有校準與測試證據時不得宣稱其他配置可用。

## 取代範圍

本決策取代 ADR 0004、0005、0008、0009 中允許遊戲鍵盤輸入、`InvokeBinding` 或 Dashboard 遊戲按鍵綁定的部分。
