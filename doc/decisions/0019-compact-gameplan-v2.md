# ADR 0019：Compact GamePlan v2

## 狀態

Accepted — 2026-08-16。`compact-v2` 已實作為候選 wire contract，但尚未通過 paired A/B Promotion Gate；正式預設仍為 `legacy-v1`。

## 背景

單一 snapshot smoke 的 `legacy-v1` Major request 產生 527～572 completion tokens，llama.cpp `predicted_ms` 約 9.5～10.2 秒，佔約 95% E2E；`prompt_ms` 多為 0.3～0.5 秒。影像 token 下降未轉換成 E2E 改善，主瓶頸是模型輸出三層 DecisionNode 與重複自然語言。

這些 smoke 都是 Major scope；依 ADR 0013，Medium 只輸出 medium+minor，Minor 只輸出 minor。因此 token 與 latency 必須按 scope 分開評估，不得把 Major completion 外推成所有 live request。

## 決策

保留 Major／Medium／Minor 決策語意與 scope merge，移除三層作文。新增版本化 `compact-v2` wire contract：

```json
{
  "action": "BuildHouse",
  "confidence": 0.91,
  "recheckMs": 1500,
  "reason": "PopulationCap",
  "raise": "None",
  "minor": "PreventPopulationBlock",
  "medium": "GrowEconomy",
  "major": "AdvanceAge"
}
```

- action、reason 與三層 intent 都是封閉 enum，沒有 `Other` 或自由文字逃生口；新增語意必須建立 contract revision。
- Minor 永遠輸出；Medium/Major 只在該 scope 允許時出現。`raise` 只允許 `None` 或比目前更高的 scope。
- action、minor intent 與 reason code 必須通過 deterministic 組合驗證；未知、缺漏或矛盾一律 fail closed。
- target age 來自使用者 directive，不由模型輸出或改寫。
- `quantity` 移除；現行程序是單一原子動作，Registry 並未消費模型 quantity。
- assessment、goal、自然語言 reason/evidence、expectedResult、completion/failure condition 與 nodeId 不再由模型生成。
- deterministic presentation adapter 只做 enum／confirmed GameState／Registry 語意至既有 GamePlan 顯示欄位的轉換，不得選動作或推導新策略。NodeId 由 level+intent+action 穩定產生。
- 第一階段保留既有內部 `GamePlan`、Validator、Automation 與 UI；`CompactGamePlanResponse -> adapter -> GamePlan`。待 v2 通過後，才以後續 ADR 清理內部自然語言欄位。

## Token budget

| Scope | Target median | Promotion ceiling | Hard cap |
|---|---:|---:|---:|
| Minor | 64 | 96 | 160 |
| Medium | 96 | 128 | 192 |
| Major | 96 | 200 | 256 |

Target 是正常表現；Promotion ceiling 是不得超過的 Gate。若 Major 經常接近 200，必須調查額外輸出，即使尚未撞到 hard cap。

## Benchmark 與 promotion

`gameplan-contract-report` 固定 image pipeline 為 `legacy-3-1024-v1`，只比較 `legacy-v1` 與 `compact-v2`。報告按 Major／Medium／Minor 分開保存 completion tokens、`predicted_ms`、E2E、action parity、品質與 scope budget。

每個 scope 必須 coverage 完整、零品質／安全退化、action parity 通過、completion 不超過 ceiling/hard cap，且 completion、decode、E2E median 都優於 v1，才可 promotion。contract version 與 image preset 必須分離；contract-v2 通過後才可另行測試 `compact-v2 + event-panel-640-v1`。

## 相容與失敗行為

- `GamePlanContractId` 明確選擇 contract；正式預設與 fallback 均維持 `legacy-v1`，不做錯誤後自動猜測或偷偷切換。
- telemetry 保存 contract id/revision、scope 與 hard cap，不保存 screenshot、base64 或 raw completion。
- v2 解析、enum、scope、語意組合、adapter 或既有 Validator 任一步失敗，整份 planning request 不接受，也不得觸發 automation。

## 後果

- 代表性手寫 Major v2 JSON 約 48 tokens，Minor 約 38～42 tokens；固定 legacy image 的單一 snapshot smoke 實測 median 為 Major 78、Medium 68、Minor 58。這仍不是 coverage-complete promotion 證據。
- decode latency 預期顯著下降，但不得在 paired A/B 前宣稱實際改善或改正式預設。
- presentation formatter 是表示層，不是第二策略來源；LLM 仍是唯一選擇 action 與 intent 的決策者。
