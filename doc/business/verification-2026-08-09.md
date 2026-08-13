# AgePilot 驗證紀錄 — 2026-08-09

## 歷史自動診斷（不作目前實機驗收）

執行環境可見 12 個 logical processors。命令與結果：

```text
dotnet build AgePilot.sln --no-restore
結果：成功，0 warnings，0 errors
```

`AgePilot.Tests` 用於確定性 OCR、快取、Gate 與規劃回歸；它不能證明 App 已在遊戲端實際完成操作。實機行為仍須由使用者操作對局驗收。

2026-08-14 回歸結果為 14/14：包含人口 `/` 誤判與遺失修復、歧義拆分 fail-closed、人口專用 OCR 前處理路徑、修復值需兩次獨立掃描、OCR 失敗下一幀重試、67.2% 人口成對確認、矛盾候選維持 unavailable、人口 Gate、房屋壓力重規劃、LLM 動作白名單、Live Trace 移除、舊設定清理及四張 Screenshot manifest OCR ground truth。

## Vision Benchmark

```text
Samples                       4
FieldExactAccuracy            100.00%
FrameExactAccuracy            100.00%
HighConfidenceErrorRate       0.000%
UnavailableRate               0.00%
FalseRecommendationRate       0.00%
RecommendationExactRate       100.00%
OCR latency average / p95     647.7 / 837.1 ms（單次量測；會受 warm-up 影響）
```

四張樣本只有黑暗時代，統計樣本不足，因此不能宣稱完整 Vision Gate 通過。

## 200-frame Replay 壓力測試

```text
Frames / failures             200 / 0
Paused frames suppressed      50 / 50
Average OCR regions / frame   5.94
OCR latency average / p95     571.6 / 666.8 ms
Average CPU                   48.07%
Peak working set              508.3 MB
```

Replay 刻意不等待、持續以最大吞吐處理，且記憶體中同時保留四張 2560×1440 BGRA Frame。這證明目前資料集連續回放不 Crash，但 CPU、記憶體與延遲不符合 v2 初始目標；必須再用實際 Live Capture 週期量測，不能把 Replay 數字包裝成實機通過。

## 已驗證產品行為

- 使用者提供的正常遊戲畫面與 Overlay 串接成功。
- 使用者提供的主選單暫停畫面可辨識，暫停時不產生建議。
- 發佈 ZIP 解壓後可執行 OCR，Dashboard 視窗成功開啟。
- 可攜版包含 README、Privacy 與 HUD Profile。
- GUI smoke：關閉 Dashboard 後程序仍存活且視窗從工作列消失。
- GUI smoke：最小化 Dashboard 後程序仍存活且視窗從工作列消失。
- GUI smoke：啟動 Overlay 後 Dashboard 為 off-screen，AgePilot 程序與 Overlay 持續運作。
- 老鷹品牌圖示已內嵌至 EXE、WPF 視窗與系統匣；發佈版 EXE 可讀取 32×32 shell icon，來源 ICO 含 16、24、32、48、64、128、256 px。
- 自動模式預設關閉，Overlay 顯示狀態與切換按鈕；可自訂開啟／緊急停止熱鍵與經濟／軍事序列。輸入層會在 AOE2 非前景時 fail closed。
- 自動操作回歸不能證明遊戲端實際作動。現行驗收由使用者操作實際對局，確認遊戲畫面結果，並以一般診斷日誌中的 LLM 決策、`automation.sent`、OCR 前後值與 `automation.confirmed` 對照；程式不保存遊戲畫面。

最終可攜版：

```text
artifacts/packages/AgePilot-public-alpha-win-x64.zip
SHA-256 236A3C8FBE73812D650AEC2B690048A5953E6CB1828400FCB915969B3DBEF598
```

## 尚缺的外部證據

- 封建、城堡與帝王時代的 ground truth。
- 五局完整實機 Session 與錯誤／洗版標註。
- Live AOE2 同時執行時的 AgePilot process CPU、Working Set、OCR latency 與遊戲 FPS 基準差。
- 全新 Windows x64 測試機驗證。
- 發佈當日政策檢查。

已提供 `live-benchmark` 命令收集前三項實機效能資料；本次驗證時 AOE2 DE 視窗不存在，命令正確以 exit code 2 fail closed，因此沒有捏造 Live 報告。
