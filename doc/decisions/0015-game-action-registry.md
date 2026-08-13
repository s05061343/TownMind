# ADR 0015：具名遊戲動作註冊表與程式化結果驗證

## 狀態

Accepted — 2026-08-13

取代 ADR 0009 中「模型直接選擇通用原子工具」與「不得重新啟用語意動作轉譯」的部分。
修訂 ADR 0010 的純滑鼠限制。
移除 ADR 0014 的座標網格疊圖。

## 背景

2026-08-13 三場實機 session（`agepilot.jsonl`，00:01–06:46 UTC）的證據：

| 指標 | 數值 |
|---|---|
| VLM 決策次數 | 18 |
| 實際送出點擊 | 6 |
| `Observe`／`Wait`（不產生任何滑鼠移動） | 10 |
| `PreviousActionResult == Confirmed` | **0** |
| 點擊座標 | `0.05,0.1` ×4、`0.5,0.5` ×2、`0.1,0.1` ×1、`0.45,0.45` ×1 |
| `planning.failure`（JSON 截斷） | 4 |

三個症狀有共同根因：**動作詞彙的抽象層級錯誤**。

`VisualToolKind { Observe, Wait, LeftClick, RightClick, Drag }` 是輸入原語，不是遊戲動作。模型被要求用滑鼠事件即興組合出遊戲語意，因此：

1. **確認迴圈結構上不可達。** 模型自己填的 `expectedResult` 是「Age changed to Castle Age」「Population increased by 10」這類跨數分鐘的策略結果，而動作只是單一次點擊。下一輪必然回 `Failed`／`Uncertain`，`FailOnce` 累積三次即 `Disable`。三場 session 全部在 3 分鐘內結束。
2. **座標是幻覺。** 模型每輪從零猜一次 normalized 座標。`0.05,0.1` 連點 4 次；該處是樹，AOE2 中左鍵點樹不選取任何東西，畫面零變化，因此也無從確認。
3. **JSON 截斷。** `assessment` 等欄位無 `maxLength`，撐爆 `max_tokens = 1024`，輸出在 4735／5021 byte 處被切斷。

### 既有的因果鏈

ADR 0010（2026-08-12）禁止對遊戲送出鍵盤 → 當時語意動作層的實作機制全部是 `ExecutableActionKind.KeyboardSequence`（見 `GamePlanExecution.cs`），失去實作手段 → ADR 0009 宣告改用通用原子工具並禁止復原 → commit `05cf39f`（2026-08-12 23:37）刪除 `ActionExecution.cs`、`AutomationPolicy.cs`、`GamePlanExecution.cs`、`GenericEconomicPlanner.cs`、`WorldObservation.cs`、`GenericWorldAnalyzer.cs`、`Aoe2InstallationCatalog.cs` → 模型被迫直接產座標 → 上述失敗。

被刪除的 `EconomicActionKind` 同時帶著兩個現在缺失的機制：程式可判定的確認條件（`Confirmation: "PopulationCapIncrease" / "AgeFeudal" / "WoodDecrease"`），以及座標跨幀穩定性閘門（`WorldTarget.IsActionable => Evidence == Verified && ConsistentFrames >= 3 && Confidence >= 0.9`）。

## 決策

### 具名動作註冊表

LLM 只輸出具名的 `GameActionKind` 與類別化參數，**永遠不輸出任何像素或 normalized 座標**。`VisualToolAction` 的 `x`／`y`／`endX`／`endY`／`row`／`column`／`space` 全部從模型輸出 schema 移除。

每個 `GameActionKind` 對應註冊表中一段寫死、測過的 `GameActionProcedure`，由 `ProcedureStep` 序列組成。程序負責「怎麼做」——按哪個鍵、點哪個格位、如何定位世界目標；LLM 只負責「做什麼」。

這**不是**第二個策略來源。ADR 0009 禁止 `AutomationPolicy`／`GenericEconomicPlanner` 的理由是避免兩個決策者衝突，此顧慮成立且本決策予以維持：LLM 仍是唯一決定「現在該做哪件事」的來源，註冊表沒有任何自主決策權，不會在 LLM 未選擇時自行發動動作。註冊表是同一決策者的執行層。

未綁定按鍵、未解析出目標或前置條件不足時，`TryResolve` 必須 fail closed 回傳 `blockedReason`，絕不可送出半套序列。

### 程式化結果驗證

`PreviousActionResult` 改由 `ActionOutcomeVerifier` 依 OCR 觀測決定，不再詢問模型；`previousActionResult` 從模型輸出 schema 移除。

每個動作在註冊表宣告 postcondition，以送出動作時的 baseline `GameState` 與其後的 `GameState`／`GameHistory` 比對：

| Postcondition | 判定依據 |
|---|---|
| `PopulationIncrease` | `GameState.Population` 增加 |
| `AgeAdvanced` | `GameState.Age` 改變 |
| `PopulationCapIncrease` | `GameState.PopulationCap` 增加 |
| `GatherRateIncrease` | `GameHistorySummarizer` 的 `ResourceTrend.ChangePerMinute` 上升 |

OCR 不可靠（`ObservedValue.IsUsable == false`）時回 `Uncertain` 並繼續等待，不得判成 `Failed`；只有逾時才是 `Failed`。

### 鍵盤輸入

允許對 AOE2 送出遊戲快捷鍵。`policy-review-2026-08-09.md` 的產品邊界是「自動操作使用經獨立審查的非侵入式外部控制後端⋯目前已實作後端為 Windows `SendInput`」；以 `SendInput` 送鍵盤與送滑鼠同屬非侵入式外部控制，該政策查核未區分兩者。ADR 0010 原文未載明禁鍵盤的理由。

ADR 0010 的其餘安全機制全部保留且不得放寬：每次程序啟動後的獨立滑鼠實機測試、`MouseCapabilitySession` 對目前視窗 Handle 的綁定、送出輸入前的前景視窗檢查、游標位置讀回、每分鐘頻率上限、連續三次失敗自動停用、Preview／Armed 與停止熱鍵。

送鍵使用 `KEYEVENTF_SCANCODE`；虛擬鍵碼常被 DirectInput 遊戲忽略。

AOE2 DE 允許使用者自訂快捷鍵，因此綁定一律由 `config/game-hotkeys/*.json` 提供，不得硬編碼。每個設定項對應遊戲快捷鍵清單中的一筆，讓使用者可以逐項對照驗證；需要多個按鍵的動作由註冊表組合，設定檔中不寫複合序列。

按鍵序列以 `>` 分隔（`H>Q`），不可用逗號——`,` 本身就是 AOE2 的按鍵（Go to Next Idle Military Unit）。修飾鍵以 `+` 相接（`Shift+.`）。

`EnableGameKeyboardInput` 預設關閉，使用者必須先在遊戲中實際按過確認有效才可開啟。**未經使用者實機確認的綁定不得宣稱可用**；由 `.hkp` 解碼得到的鍵位只是佐證，不等於實機驗證。

### 移除座標網格疊圖

ADR 0014 在 panorama／minimap 疊加半透明座標網格，目的是輔助模型估算連續座標。模型不再輸出座標後此輔助失去用途，予以移除。ADR 0014 的 `PreviousActionResult.NotApplicable` fail-closed 處理由本決策的程式化驗證取代。

### 首批動作範圍

依「是否需要世界座標接地」分兩期。Phase 1 必須先實機驗證 `Confirmed` 真的出現，才實作 Phase 2；若閉環仍關不起來，代表根因不在動作詞彙層。

| 動作 | 接地需求 | 期別 |
|---|---|---|
| `Observe`、`Wait` | 無 | 1 |
| `QueueVillager` | `H` 選城鎮中心 → `Q` 生產村民；**純快捷鍵，零座標** | 1 |
| `AdvanceAge` | `H` 選城鎮中心 → `Z` 升時代；**純快捷鍵，零座標** | 1 |
| `GatherFood`／`GatherWood`／`GatherGold` | 選閒置村民（快捷鍵）＋右鍵世界資源 | 2 |
| `BuildHouse` | 選村民＋建造鍵＋點空地 | 2 |

Phase 1 兩個動作原先設計為「快捷鍵選城鎮中心＋滑鼠點命令面板格位」，格位座標為實作時虛構。2026-08-13 取得使用者實際鍵位後修正：解碼 `快速鍵TOM.hkp` 與 `快速鍵TOM\Base.hkp`（兩檔互補，合併為遊戲生效的完整設定）確認生產村民（`Q`，StringID 19054）與升時代（`Z`，StringID 19336）都有鍵盤快捷鍵，因此改為純鍵盤，不再依賴 `CommandGridRegion` 校準。虛構的格位設定已移除。

命令面板點擊機制（`ProcedureStepKind.CommandGridClick`）保留但 Phase 1 無人使用，供 Phase 2 遇到沒有快捷鍵的指令時作為後備。

`Q` 在 AOE2 中是上下文相關的（村民選中時為 Economic Buildings、僧侶為 Monk），因此選取與命令之間必須保留等待面板重繪的延遲步驟。

Phase 2 還原 `WorldTargetKind`／`WorldTargetEvidence`／`WorldTarget` 型別並保留 `IsActionable` 的跨幀穩定性閘門，但**不得照抄** `GenericWorldAnalyzer` 的顏色閾值分類（`g > 55 && g > r * 1.18` 這類判斷換地圖、季節貼圖或文明配色即失效）。若定位無法穩定達到 `ConsistentFrames >= 3 && Confidence >= 0.9`，應停在只做零座標動作，不得放寬閘門。

## 後果

- 模型的決策粒度從「滑鼠往哪裡點」提升到「現在該做哪件事」，後者才是它有能力做的判斷。
- 確認迴圈由 OCR 判定，`Confirmed` 從結構上不可達變為可達。驗收指標為 `agepilot.jsonl` 出現 `Confirmed`（至今從未出現）。
- 程序層可使用模型做不到的手段：快取、重試、跨幀比對、一次性校準。
- 座標幻覺對 Phase 1 動作完全消失；Phase 2 仍有風險，由穩定性閘門承擔。
- 移除模型輸出的座標欄位與加上 `maxLength` 後，單次回覆長度大幅下降，JSON 截斷應歸零。

## 已知限制

- ADR 0009 宣稱「靜態遊戲資料負責成本、前置條件與文明合法性」，但 `Aoe2InstallationCatalog.cs` 已於 commit `05cf39f` 刪除，專案目前**沒有任何靜態遊戲資料實作**。Phase 1 的成本門檻改用 OCR 資源值與註冊表內的常數；此為已知缺口，不得宣稱具備文明合法性檢查。
- 快捷鍵綁定的正確性依賴使用者確認，程式無法自行驗證遊戲內設定。
- 校準環境仍限 2560×1440、全螢幕、繁中 UI、HUD 50%。
