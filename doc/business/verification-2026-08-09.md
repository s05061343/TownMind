# AgePilot 驗證紀錄 — 2026-08-09

## 自動驗證

執行環境可見 12 個 logical processors。命令與結果：

```text
dotnet build AgePilot.sln --no-restore
結果：成功，0 warnings，0 errors

dotnet run --no-build --project tests/AgePilot.Tests
結果：28/28 passed
```

測試涵蓋 ROI 映射、Profile、解析器、四張實際 Screenshot OCR、Vision 指標、暫停辨識、Adaptive ROI cache、生命週期、Temporal confirmation、Farmer Rules、Recommendation lifecycle、設定、JSONL 日誌與 SQLite round-trip。

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

最終可攜版：

```text
artifacts/packages/AgePilot-public-alpha-win-x64.zip
SHA-256 7956ECE990754AAD6C42E9FCF0A5F749EB5D12CE2E93988435858B1780EAED6B
```

## 尚缺的外部證據

- 封建、城堡與帝王時代的 ground truth。
- 五局完整實機 Session 與錯誤／洗版標註。
- Live AOE2 同時執行時的 AgePilot process CPU、Working Set、OCR latency 與遊戲 FPS 基準差。
- 全新 Windows x64 測試機驗證。
- 發佈當日政策檢查。

已提供 `live-benchmark` 命令收集前三項實機效能資料；本次驗證時 AOE2 DE 視窗不存在，命令正確以 exit code 2 fail closed，因此沒有捏造 Live 報告。
