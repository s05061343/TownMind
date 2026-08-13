# ADR 0013：依範圍限定的階層決策 Schema，並清除舊版扁平計畫殘留

## 狀態

Accepted — 2026-08-13

## 背景

`LlamaServerPlanner.PlanAsync` 過去要求 Qwen3-VL 每次呼叫都完整重新生成 Major、Medium、Minor 三層 `DecisionNode`（每層最多 5 個 300 字內文字欄位），即使 `allowedUpdateScope` 表示這一輪只有 Minor 可以變動——生產環境實測延遲落在 9-24 秒，`LlmPlanningTimeoutSeconds` 預設只有 30 秒。程式原本用 `FrozenParentsMatch` 比對前後兩輪 `DecisionNode` 是否逐欄位相等，來判斷模型有沒有「越級改寫」凍結的父層；但模型每次都是重新生成自由文字，即使指示「原樣保留」也幾乎不可能一字不差，導致 `allowedUpdateScope < Major` 的請求幾乎必然被判定越級改寫、整份計畫作廢，觸發 10 秒退避重試，在生產 log 中已實測到。

另外，`nodeId` 沒有格式規範傳達給模型（schema 沒有 `pattern`、prompt 沒說明），但驗證器要求 ASCII 英數字/`-`/`_`、三層互不相同，中文模型容易生成非 ASCII 節點 ID 而觸發「階層計畫節點無效」。

同時，ADR 0008/0009 時代的扁平計畫機制（`PlannedAction`／`PlannedActionKind`／`PlanCondition`／`GamePlan.Actions`／`Assumptions`／`MissingInformation`）自 ADR 0010/0011 改為 VLM 視覺決策驅動後，只靠一個假的 `Reobserve` 佔位動作撐著驗證器的非空檢查，已無實際規劃功能，`LlamaServerPlanner.NormalizeQuantities` 更是零生產呼叫端的死代碼。

## 決策

`LlamaServerPlanner` 的回覆 JSON schema 改由 `BuildResponseFormat(PlanUpdateScope scope)` 依範圍動態產生：`minorDecision` 永遠必填；`mediumDecision` 只在 `scope >= Medium` 時列入；`majorDecision` 只在 `scope >= Major` 時列入。`additionalProperties: false` 維持不變，模型在 schema 結構上就不可能輸出凍結層的欄位——不再需要任何逐欄位相等比對，`FrozenParentsMatch` 直接刪除。凍結層的內容改由程式端的 `AssembleDecisions` 從 `previousPlan` 複製帶入。若請求時沒有 `previousPlan`（例如第一次呼叫），一律以 Major 範圍處理，寧可多花時間重建全部，也不 fail-closed 報廢整輪。`StrategyEngine.Request` 同步補上防呆：`_current` 尚未成功建立前，任何範圍請求都會被夾到 Major，避免「Major 規劃失敗又被較低範圍事件覆寫」造成永久卡在 Minor＋無前一份計畫的重試迴圈。

`DecisionNodeSchema()` 的 `nodeId` 加上 `pattern: "^[A-Za-z0-9_-]{1,80}$"`，system prompt 改為資料驅動：新增 `outputFields`（這輪允許輸出的層級）與 `frozenDecisions`（凍結層內容與已占用的 nodeId），取代原本靜態描述三種情境的文字。

清除 `PlannedAction`／`PlannedActionKind`／`PlanCondition`（`PlanningModels.cs`）、`GamePlanValidator` 對應的驗證區塊、`GamePlan.Actions`／`Assumptions`／`MissingInformation`、`LlamaServerPlanner.NormalizeQuantities`。`GamePlanRecommendationAdapter.Convert` 改為從 `plan.MinorDecision` 建立 `Recommendation`（類別與呼叫端不變），並順便修正原本用 `PlanId`（每輪新 GUID）當 id 導致 dismiss 形同虛設、資料庫每輪新增一列的問題，改用 `minor.NodeId`。`GamePlanValidator` 的階層檢查改為無條件必填（`MajorDecision`/`MediumDecision`/`MinorDecision` 皆不可為 null），取代原本「三層全為 null 也算合法」的漏洞（該漏洞原本是靠已刪除的 `Actions.Count == 0` 檢查間接把關）。

視覺輸入額外拿掉重複的 `top_hud` 裁切圖（資源數值已由 OCR 以文字送入 prompt），llama-server 啟動參數把 `--image-min-tokens` 從 1024 降到 256（`--image-max-tokens` 維持 1024），讓遠比全景圖小的 `command_panel`／`minimap` 裁切圖不必被灌到跟全景圖一樣的 token 數。

## 後果

- Minor/Medium 範圍的規劃請求輸出 token 明顯減少，且不再因為模型重新措辭凍結層文字而假性失敗。
- `GamePlanValidator`、`GamePlanRecommendationAdapter`、`LlamaServerPlanner` 不再帶著只服務假占位動作的驗證與轉換邏輯。
- `nodeId` 格式與跨層唯一性風險降低，但驗證器的 ASCII／唯一性檢查仍是最終防線，不因為 schema 端是否生效而改變行為。
- 本決策取代 ADR 0008 中量化動作（`Actions`／`PlannedAction`）與其 UI 呈現規則的部分，以及 ADR 0011 中「凍結父層驗證由 merge 與逐欄位相等比對強制執行」的部分——現在改由 schema 結構本身保證模型無法輸出凍結層。
