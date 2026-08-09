# Phase 0 執行指南

## 第一次執行

在 repository root 開啟 PowerShell：

```powershell
dotnet restore AgePilot.sln
dotnet build AgePilot.sln --no-restore
```

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

測試包含參考遊戲截圖的真實 PaddleOCR 回歸。

## 啟動 Playable Prototype Overlay

先啟動 AOE2 並進入對局，再執行：

```powershell
dotnet run --no-build --project src\AgePilot.App -- overlay `
  "config\hud\aoe2de-zh-tw-2560x1440-50.json"
```

Overlay 會持續更新資源、人口與最高優先建議。可拖曳視窗，右上角 `×` 可安全停止監測並關閉。

## 已知限制

- Dashboard 尚未完成；目前提供 Compact WPF Overlay。
- `scan-live` 是單幀掃描；持續刷新請使用 `overlay`。
- GDI Capture 已通過 2560×1440 全螢幕實機驗證；其他顯示模式仍未驗證。
- 單一截圖通過不等於 Vision Gate 已通過，仍需要不同資源值與時代的樣本。
