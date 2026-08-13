# ADR 0012：視窗截圖改用 PrintWindow，並在送出滑鼠前主動恢復前景

## 狀態

Accepted — 2026-08-13

## 背景

診斷日誌（`%LocalAppData%\AgePilot\logs\agepilot.jsonl`）顯示，啟用自動操作後 Qwen3-VL 的 `automation.decision` 事件持續回傳 `Tool=Observe`，理由（`Target` 欄位）直接寫出「AgePilot overlay is still active and the game window is not fully visible」「遊戲畫面中存在 AgePilot 瀏覽器視窗與 ASUS 網站視窗，可能干擾遊戲操作」。追查後確認 `WindowsGdiFrameCapture` 原本用 `GetDC(nint.Zero)` 對桌面 DC 做 `BitBlt`，擷取的是遊戲視窗座標範圍內「螢幕上看到的畫面」，而不是遊戲視窗自己渲染的內容。AgePilot 自己的 Topmost Overlay（見 ADR 0008）與其他疊加視窗因此被一併拍進送給 VLM 的畫面，導致 VLM 依 fail-closed 原則永遠不敢送出點擊。

## 決策

視窗截圖（`WindowsGdiFrameCapture.CaptureAsync`）改用 `PrintWindow(window.Handle, memoryDc, PW_RENDERFULLCONTENT)`，直接向目標視窗要求把渲染內容畫進擷取用的 memory DC，不再經過桌面 DC。`PW_RENDERFULLCONTENT` 是 Windows 8.1 起提供、給 DirectX／UWP 全螢幕應用使用的旗標，擷取結果只反映遊戲視窗自身畫面，不受任何疊加在其上的視窗（AgePilot Overlay、瀏覽器、疊加軟體等）影響。`PrintWindow` 失敗時直接拋出例外，不退回舊的桌面 `BitBlt` 方式，避免又把疊加視窗拍回畫面。

正式滑鼠事件送出前的前景檢查（`WindowsInputSender.TryMove`）補上與滑鼠測試探針（`TryPrepareProbe`）相同的恢復邏輯：若 `GetForegroundWindow()` 不是遊戲視窗，先呼叫一次 `SetForegroundWindow` 並短暫等待，再重新檢查；仍不是前景才 fail closed，不呼叫 `SendInput`。因為 Overlay 是 Topmost 視窗，使用者點擊 Overlay 上的按鈕、拖曳標題列或切換滑鼠穿透時，原本會使 Overlay 短暫成為前景視窗，導致下一次滑鼠動作被擋下；因此 Overlay 視窗額外加上擴充樣式 `WS_EX_NOACTIVATE`，讓這些互動不會讓 Overlay 取得前景/作用中狀態，但按鈕點擊與拖曳仍正常運作。

## 後果

- 截圖不再受 AgePilot 自身 UI 或其他疊加視窗干擾，VLM 才有機會依實際遊戲畫面判斷並送出 `LeftClick`／`RightClick`／`Drag`。
- 前景恢復邏輯不放寬 ADR 0010 的 fail-closed 前提：恢復嘗試後仍非前景一律阻擋，不送出 `SendInput`。
- `PrintWindow` 對極少數渲染路徑仍可能回傳失敗或黑畫面；目前僅在 2560×1440、全螢幕、DirectX 11（AOE2 DE）環境下驗證，其餘顯示模式沿用既有「未驗證」限制。
