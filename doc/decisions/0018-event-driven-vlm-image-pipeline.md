# ADR 0018：事件驅動 VLM 影像 Pipeline

## 狀態

Accepted — 2026-08-15。候選 preset 尚未通過 Promotion Gate；正式預設仍是 `legacy-3-1024-v1`。

## 背景

舊 pipeline 每個 OCR scan 都把完整畫面縮成 1536×864，另附原生 command panel 與 minimap。三張影像內容重疊，且即使沒有規劃事件也會執行 JPEG 編碼。llama.cpp 對每張影像設定 256～1024 tokens，因此三張影像的理論 visual-token 範圍為 768～3072。

## 決策

- 正式預設在驗證前維持 immutable `legacy-3-1024-v1`，但所有 preset 都改為只有真正建立 planning request 時才裁切與 JPEG 編碼。
- 候選 pipeline 常駐 `battlefield` 與 `minimap`；`command_panel` 由 bootstrap、dirty、action context 或 accepted-send TTL 條件式加入。
- Battlefield 使用 HUD profile 的 normalized ROI；目前只校準 2560×1440、全螢幕、繁中、HUD 50%。裁切後等比例縮放，不拉伸。
- Panel policy 分離 raw、candidate、stable、attempted、accepted 與 dirty。Accepted 表示 panel 參與了一份通過 HTTP、JSON、schema 與 `GamePlanValidator` 的計畫，不只是 server 收到 request。
- 初始 event-panel v1 使用 64-bit DCT pHash、candidate tolerance 2、stable 750 ms／至少兩次觀察、dirty threshold 10、accepted TTL 15 秒。任何參數變更必須建立新 preset revision。
- hash 與 TTL 不自行觸發 LLM，只控制下一次既有事件規劃是否附 panel。
- llama.cpp response telemetry 保存 total usage、timings、影像尺寸與 AgePilot crop／resize／JPEG／serialize／HTTP latency，不保存 screenshot 或 base64。

## Benchmark 與 Promotion

`vlm-ab-report` 使用 snapshot corpus 做真實 Qwen paired A/B；`vlm-sequence-report` 使用 fake clock 與 deterministic event sequences 驗證 state machine。報告分開呈現 2-image、3-image、overall 與 inclusion-reason latency。

候選必須零關鍵品質／安全退化、total prompt tokens 下降，並同時通過：

```text
CandidateMedian <= min(6.0s, BaselineMedian × 0.65)
CandidateP95    <= min(12.0s, BaselineP95)
```

逐圖 token 若 runtime 未提供可標示 unavailable 或 isolated estimate，不阻止 promotion；total prompt tokens 與 E2E timing 必須可量測。缺少 manifest coverage tag 時一律不得 promotion。

## 後果

- OCR scan 不再等同 JPEG encode；沒有規劃事件時不建立視覺 request payload。
- request 失敗不會錯誤刷新 panel evidence；pending 期間的新 stable state 也不會被舊 request 清除。
- `640`、`512` 與 battlefield 1536／1280／1024 都只是實驗候選，未通過 Gate 前不得改成正式預設。
- 離線 VLM pipeline 證據不能用來宣稱遊戲自動操作成功；automation 仍須 user-operated live trace。
