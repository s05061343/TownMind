# Phase 0 執行指南

## LLM 規劃預覽

Portable 套件不包含約 5 GB 的 GGUF 模型或 llama.cpp runtime。第一次使用請在 Dashboard 的「LLM 規劃預覽」區選擇：

- Runtime 目錄：包含 `hip/llama-server.exe` 與 `vulkan/llama-server.exe` 的 `llama.cpp` 目錄。
- GGUF 主模型：`Qwen3VL-8B-Instruct-Q4_K_M.gguf`。
- 視覺編碼器：`mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf`。

按「測試 LLM」後，狀態顯示「已就緒」才代表 backend、模型與圖片理解成功。自動操作預設為預演；只有使用者在 Overlay 明確啟用後才會送出遊戲輸入。

Dashboard 可選擇最終目標時代（封建／城堡／帝王，預設城堡）。每次啟動 AgePilot 後還必須在 AOE2 已開啟時按「測試滑鼠」；游標會短暫移動並回到原位，全程不點擊。只有移動與恢復座標都經 Win32 讀回確認後，Overlay 才允許啟用自動操作。AOE2 視窗重開後必須重新測試。

HIP 狀態必須顯示實際裝置，例如 `HIP / ROCm0`。若只載入 CPU backend，AgePilot 會拒絕啟動規劃；請確認 AMD ROCm 已安裝，且其 `bin` 目錄包含 `amdhip64_7.dll`。

## 第一次執行

在 repository root 開啟 PowerShell：

```powershell
dotnet restore AgePilot.sln
dotnet build AgePilot.sln --no-restore
```

建置後直接啟動 Dashboard：

```powershell
dotnet run --no-build --project src\AgePilot.App
```

Dashboard 可保存 HUD Profile、Overlay 透明度與掃描間隔，並直接啟動／停止 Overlay。設定儲存在 `%LocalAppData%\AgePilot\settings.json`。

## System Tray 行為

- 按 Dashboard 的關閉按鈕不會結束程式，而是隱藏到右下角通知區。
- 最小化 Dashboard 也會隱藏到通知區，不留在工作列。
- 啟動 Overlay 後 Dashboard 自動隱藏，監看與通知區圖示繼續運作。
- 關閉 Overlay 只停止監看；AgePilot 仍留在通知區。
- 雙擊通知區圖示可重新開啟 Dashboard。
- 通知區右鍵選單可開啟 Dashboard、啟動／停止 Overlay。
- 只有通知區右鍵選單的「結束 AgePilot」會真正退出程式。
- Windows 登出或關機仍可正常結束，不會攔截系統關閉。

對局歷史儲存在 `%LocalAppData%\AgePilot\agepilot.db`。Dashboard 顯示最近五局的時間、持續時間、Snapshot 數與建議數；AgePilot 不會把 Screenshot 寫入資料庫。

「儲存本機對局紀錄」可隨時關閉。診斷匯出只有按下「匯出診斷」並選定檔案後才會建立 JSON；內容不含 Screenshot、按鍵內容或帳號名稱。

「儲存本機診斷事件」可控制 `%LocalAppData%\AgePilot\logs\agepilot.jsonl`。日誌包含 PID、執行緒、生命週期、規劃、自動操作與全域例外；單檔達 5 MB 後輪替一次，不保存 Screenshot。若主檔因權限或 I/O 問題無法寫入，改寫同目錄的 `agepilot-emergency.log`。

## 執行可攜版

1. 解壓 `artifacts\packages\AgePilot-public-alpha-win-x64.zip`。
2. 雙擊 `AgePilot.App.exe`。
3. 保持預設 HUD Profile，啟動 AOE2 DE 並進入對局。
4. 在 Dashboard 按「啟動 Overlay」。

Overlay 啟動後 Dashboard 會自動收到通知區；不需要保持 Dashboard 視窗開啟。

## 自動操作

1. 在 Dashboard 檢查「語意綁定」；格式為每行 `bindingId=按鍵序列`，預設採 AOE2 DE Grid 鍵位。
2. 儲存設定後重新啟動 Overlay，使熱鍵與綁定生效。
3. 切回 AOE2，按 `Ctrl+F10` 或 Overlay 的「啟用」。
4. 啟用後 Overlay 縮成仍可點擊的控制條；按「停止」或 `Ctrl+F12` 可立即停止。
5. 若停止熱鍵註冊失敗，AgePilot 會拒絕啟用自動操作。

按鍵以逗號分隔，組合鍵使用 `+`，例如 `Ctrl+H,Q`。VLM 只能選擇已設定的語意綁定，不能產生任意按鍵；建築與採集位置則從完整畫面提出 normalized 座標。自動模式只在 AOE2 是前景視窗、非暫停且觀測可靠時送出輸入。

每次只執行一個原子動作，等待指定時間後擷取新畫面，由 VLM 回報前一動作 `Confirmed`、`Failed` 或 `Uncertain`；確認前不會送出下一個動作。連續三次無法安全操作會自動停止。若 Windows 拒絕輸入，請確認 AOE2 與 AgePilot 使用相同權限層級。

目前只驗證 2560×1440、全螢幕、繁中 UI、HUD 50% 的標準陸地局。範圍包含村民、採集、經濟建築、經濟科技與升時代，不包含軍事生產、戰鬥、水圖或任意解析度。

一般雙擊 `AgePilot.App.exe` 會以 Windows GUI 模式啟動，不顯示終端黑窗。帶命令列參數的開發工具仍會附加到既有 PowerShell 輸出結果。

可攜版為 `win-x64` self-contained，不需要預先安裝 .NET。完整性可用同目錄 `.sha256` 檔驗證。

重新建立封裝：

```powershell
.\scripts\package.ps1
```

Overlay 的「略過」會暫時隱藏目前建議；條件解除後自動重置。若同一問題之後再次發生，AgePilot 會重新提示。

NuGet 會下載本機 PaddleOCR 模型與 Windows x64 runtime，第一次還原時間與檔案量較大；執行 OCR 時不會上傳遊戲畫面。

## 驗證參考遊戲截圖

```powershell
dotnet run --no-build --project src\AgePilot.App -- ocr-image `
  "doc\Snipaste_2026-08-09_16-29-15.jpg" `
  "config\hud\aoe2de-zh-tw-2560x1440-50.json"
```

預期值：

```text
Wood         200
Food         200
Gold         100
Stone        200
Population   4/5
```

## 掃描執行中的遊戲

1. 啟動 AOE2 DE。
2. 進入實際對局並保持遊戲視窗存在。
3. 在另一個 PowerShell 執行：

```powershell
dotnet run --no-build --project src\AgePilot.App -- scan-live `
  "config\hud\aoe2de-zh-tw-2560x1440-50.json"
```

這個命令會偵測 AOE2 視窗、擷取一幀並輸出資源與人口。現階段校正環境是 2560×1440、全螢幕、繁中、HUD 50%。

## 執行測試

```powershell
dotnet run --no-build --project tests\AgePilot.Tests
```

測試包含四張參考／實機／暫停畫面的真實 PaddleOCR 回歸。

## 產生 Vision 與 Replay 報告

```powershell
dotnet run --no-build --project src\AgePilot.App -- vision-report `
  "testdata\screenshots\manifest.json" "artifacts\reports\vision-benchmark.json"

dotnet run --no-build --project src\AgePilot.App -- replay-report `
  "testdata\screenshots\manifest.json" "artifacts\reports\replay-benchmark.json" 50

dotnet run --no-build --project src\AgePilot.App -- live-benchmark `
  "config\hud\aoe2de-zh-tw-2560x1440-50.json" "artifacts\reports\live-benchmark.json" 300
```

第一個命令量測欄位／整幀正確率、高信心錯誤率、Unavailable rate、建議一致率與錯誤建議率。第二個命令以 manifest 重複回放完整 Vision → Temporal GameState → Rules pipeline，輸出 failure、CPU、記憶體、延遲與暫停抑制數。

第三個命令必須在 AOE2 DE 對局視窗存在時執行，範例會量測五分鐘 Live Capture。遊戲不存在時會以 exit code 2 fail closed，不會建立假的成功報告。遊戲 FPS 仍需由遊戲內或外部效能工具同時記錄 baseline 與 AgePilot 開啟後的差異。

## 啟動 Playable Prototype Overlay

先啟動 AOE2 並進入對局，再執行：

```powershell
dotnet run --no-build --project src\AgePilot.App -- overlay `
  "config\hud\aoe2de-zh-tw-2560x1440-50.json"
```

Overlay 會持續更新資源、人口與最高優先建議。可拖曳視窗，右上角 `×` 可安全停止監測並關閉。

快捷鍵：

```text
Ctrl + Shift + A   顯示／隱藏 Overlay
Ctrl + Shift + C   開啟／解除滑鼠穿透
```

## 已知限制

- `scan-live` 是單幀掃描；持續刷新請使用 `overlay`。
- 視窗截圖改用 `PrintWindow(PW_RENDERFULLCONTENT)`（見 [decisions/0012](../decisions/0012-window-capture-and-foreground-focus.md)），只反映遊戲視窗自身畫面，不受 Overlay 或其他疊加視窗影響；已通過 2560×1440 全螢幕實機驗證，其他顯示模式仍未驗證。
- 現有四張樣本都屬黑暗時代；封建、城堡、帝王、Alt+Tab 與遊戲結束仍需追加實機資料。
- 已辨識主選單暫停畫面並停止建議；其他暫停型態仍需新增樣本。
