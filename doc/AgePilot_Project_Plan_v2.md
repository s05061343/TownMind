# AgePilot 專案計劃書 v2

> **A calm companion for Age of Empires II**  
> 陪你慢慢建立自己的帝國。

**文件狀態：** 執行基準草案  
**平台：** Windows  
**目標遊戲：** Age of Empires II: Definitive Edition  
**主要技術：** C# / .NET 8 / WPF  
**核心原則：** Calm、Local、Read-only、Player-first

---

## 1. 文件目的

本文件定義 AgePilot 的產品定位、可觀察資料邊界、技術架構、開發階段與驗收條件。

規劃遵守以下順序：

```text
先證明畫面資料可被可靠讀取
→ 再證明建議確實有幫助
→ 最後才擴充產品功能
```

任何後續功能都不得跳過前一階段的驗收門檻。

---

## 2. 產品摘要

AgePilot 是針對《Age of Empires II: Definitive Edition》（以下簡稱 AOE2 DE）的桌面輔助教練。

它透過遊戲視窗畫面擷取、局部圖像辨識、狀態推論與確定性規則，向玩家提供少量、平靜且可忽略的即時建議。

AgePilot 不直接操作遊戲，不注入遊戲程序，不讀取遊戲記憶體，也不修改網路資料。

一句話定位：

> **AgePilot 看著你的帝國成長，只在需要時告訴你下一步，而決定永遠留給玩家。**

### 2.1 核心差異

一般 Build Order 工具偏重速度、精準秒數與競技最佳解；AgePilot 優先考慮：

```text
經濟穩定
→ 基地安全
→ 人口與資源平衡
→ 科技發展
→ 軍事準備
→ 主動進攻
```

AgePilot 是副駕駛，不是教官。

---

## 3. 目標使用者

### 3.1 第一優先：休閒 AI 玩家

- 主要與 AI 對戰。
- 喜歡基地經營與中後期發展。
- 不追求極限 Build Order 或高 APM。
- 希望獲得提醒，但不希望被催促。

### 3.2 第二優先：新手玩家

- 經常卡人口或忘記生產村民。
- 不清楚資源是否失衡。
- 不知道何時適合升時代。
- 希望將決策簡化成「現在、下一步、為什麼」。

### 3.3 第三優先：回鍋玩家

- 已理解基本操作，但尚未恢復經濟與時代節奏。
- 不需要基礎 UI 教學，只需要適時提醒。

---

## 4. 產品原則

### 4.1 Calm

- 不用責備、催促或競技化文案。
- 不因玩家慢於標準 Build Order 就發出警告。
- 一次最多顯示一個主要任務、兩個次要任務及一個必要警告。

### 4.2 Local

- 畫面辨識預設完全在本機進行。
- 不預設上傳截圖、遊戲畫面、玩家名稱或對局內容。
- Telemetry 預設關閉；未來若加入，必須明確 Opt-in。

### 4.3 Read-only

AgePilot 只觀察玩家本來能在畫面上看到的資訊。

明確禁止：

- Hook 或 DLL Injection。
- 讀取遊戲程序記憶體。
- 修改封包或遊戲檔案。
- 發送滑鼠、鍵盤或其他遊戲輸入。
- 自動控制、生產或戰鬥。

### 4.4 Player-first

- 提供建議，不替玩家作決定。
- 支援不同玩法，不將單一玩法視為正解。
- 允許略過、關閉或降低建議頻率。

---

## 5. 非目標

下列項目不屬於早期版本：

- 自動 Build Order Bot。
- 精準辨識所有兵種與戰鬥狀態。
- 競技天梯最佳化。
- 毫秒級即時反應。
- 由 LLM 決定核心遊戲策略。
- 跨平台版本。
- 雲端帳號或遠端資料庫。

---

## 6. 最大產品假設

AgePilot 成立的前提不是 Overlay 是否漂亮，而是：

> **能否僅從畫面取得足夠可靠的資料，並避免錯誤資料產生錯誤建議。**

因此，第一優先不是功能數量，而是建立可重複測試的 Vision Pipeline、資料集和錯誤抑制機制。

若 Vision Gate 未通過，暫停規則與 UI 擴充，先改善辨識能力或縮小支援環境。

---

## 7. 可觀察資料與可靠度分層

不是所有 GameState 欄位都具有相同可信度。系統必須明確區分直接觀察、時間推論與進階視覺推論。

### 7.1 Tier A：直接觀察

早期版本優先支援：

- 食物、木材、黃金、石頭。
- 目前人口與人口上限。
- 遊戲時間。
- 時代；只有在可穩定辨識時才啟用。

Tier A 可以驅動第一批建議。

### 7.2 Tier B：時間序列推論

由多筆 Tier A 資料推論：

- 資源持續囤積。
- 人口長時間沒有變化。
- 時代轉換時間點。
- 人口封頂持續時間。

Tier B 建議必須標示推論所用時間窗與信心。

### 7.3 Tier C：進階視覺推論

後續版本才加入：

- 村民數量與經濟分配。
- TC 與其他建築數量。
- 軍事人口。
- 文明、地圖及敵方威脅。
- 單位、建築或受攻擊狀態。

依賴 Tier C 的規則，在相應辨識器通過驗收前不得啟用。

### 7.4 規則與資料依賴

每條規則必須宣告：

- 所需欄位。
- 最低欄位信心。
- 允許的資料最大年齡。
- 最短持續時間。
- 所屬可靠度層級。

不能只用單一全域 `GameState.Confidence` 決定所有規則。

---

## 8. 資料模型

資料流分成三層，避免 OCR 結果直接覆蓋遊戲狀態：

```text
RawObservation
→ ValidatedObservation
→ EstimatedGameState
```

### 8.1 Observation

```csharp
public sealed record ObservedValue<T>(
    T Value,
    double Confidence,
    DateTimeOffset ObservedAt,
    ObservationStatus Status);
```

`ObservationStatus` 建議包含：

```text
Raw
Confirmed
Rejected
Stale
Unavailable
```

### 8.2 EstimatedGameState

```csharp
public sealed class GameState
{
    public TimeSpan? GameTime { get; init; }
    public GameAge? Age { get; init; }

    public ObservedValue<int>? Food { get; init; }
    public ObservedValue<int>? Wood { get; init; }
    public ObservedValue<int>? Gold { get; init; }
    public ObservedValue<int>? Stone { get; init; }
    public ObservedValue<int>? Population { get; init; }
    public ObservedValue<int>? PopulationCap { get; init; }

    public int? Villagers { get; init; }
    public int? TownCenters { get; init; }
    public ThreatLevel? ThreatLevel { get; init; }
}
```

欄位未知時使用 `null` 或 `Unavailable`，不得用 `0` 代表未知。

---

## 9. Vision Pipeline

### 9.1 基本流程

```text
Window Detection
→ Frame Capture
→ HUD Profile / Anchor
→ ROI Crop
→ Preprocess
→ OCR / Recognition
→ Parse
→ Validate
→ Temporal Confirmation
→ Observation
```

### 9.2 第一版限制

Vision Gate 的已確認校正環境為：

- 2560×1440。
- 全螢幕。
- 繁體中文。
- HUD Scale 50%。
- 手動定義 ROI。

ROI 使用 0～1 標準化座標，可隨輸入畫面大小等比例換算。這只代表相同 HUD 佈局下的幾何映射，不代表任意 HUD Scale 或 Layout 已受支援。其他 UI Scale、HDR、超寬螢幕與 Anchor 自動校正，只有在校正環境通過後才加入。

### 9.3 更新頻率初始值

```text
Capture       5 FPS
OCR           2 FPS
Rule Engine   1 FPS
Overlay       event-driven
```

這些數值是初始假設，應由效能測試調整。

### 9.4 時間穩定化

可使用：

- 多幀一致性。
- Confidence threshold。
- Median filter。
- 欄位合法範圍。
- 前後數值與資料新鮮度。

Maximum delta 只能作為訊號，不能單獨否決讀值，因為升時代、研究、交易或建築可能造成真實的大幅資源變化。

### 9.5 Fail closed

當資料信心不足、已過期或彼此矛盾時：

```text
不產生建議
→ 保留上一筆可信狀態
→ 顯示辨識狀態異常
```

寧可少提示，也不能自信地給錯提示。

---

## 10. Vision 驗收與測試資料集

### 10.1 資料集

```text
testdata/screenshots/
  1920x1080/
    dark/
    feudal/
    castle/
    imperial/
```

每張圖片搭配 metadata：

```json
{
  "food": 812,
  "wood": 522,
  "gold": 203,
  "stone": 180,
  "population": 62,
  "populationCap": 80
}
```

資料集應包含：

- 不同時代與地圖。
- 低值、高值及位數變化。
- HUD 動畫與普通遊戲畫面。
- Alt+Tab 前後、暫停與載入狀態。
- 容易混淆的數字案例。

### 10.2 指標定義

至少追蹤：

- `FieldExactAccuracy`：單欄完全正確率。
- `FrameExactAccuracy`：同一幀所有必要欄位完全正確率。
- `HighConfidenceErrorRate`：高信心但錯誤的比例。
- `RecoveryTime`：誤讀後恢復可信狀態所需時間。
- `UnavailableRate`：系統拒絕輸出讀值的比例。
- `FalseRecommendationRate`：錯誤觀察實際造成錯誤建議的比例。

### 10.3 Vision Gate 完成條件

固定支援環境下：

- 必要資源與人口欄位的 `FieldExactAccuracy ≥ 99%`。
- `FrameExactAccuracy ≥ 97%`。
- `HighConfidenceErrorRate ≤ 0.1%`。
- 錯誤或低信心資料不得直接產生建議。
- 使用錄製或擷取資料連續回放一整局，不 Crash。
- 實機遊玩至少三局，沒有由高信心誤讀造成的嚴重錯誤建議。

門檻可在取得基準數據後調整，但調整必須留下原因與測試結果。

---

## 11. 規則引擎

第一版智慧核心採用確定性規則，不依賴 LLM。

### 11.1 Rule Contract

```csharp
public interface ICoachRule
{
    string Id { get; }
    RuleRequirements Requirements { get; }

    RuleResult Evaluate(
        GameState state,
        GameHistory history,
        PlayerProfile profile);
}
```

### 11.2 Recommendation

```csharp
public sealed record Recommendation(
    string Id,
    CoachSeverity Severity,
    string Title,
    string Description,
    string Category,
    int Priority,
    double Confidence,
    TimeSpan Cooldown);
```

Recommendation lifecycle：

```text
Created
→ Displayed
→ Acknowledged / Dismissed
→ Resolved / Expired
```

### 11.3 防洗版

每條規則至少包含：

- Minimum active duration。
- Cooldown。
- Priority suppression。
- Dismiss support。
- Resolution condition。

同時顯示上限：

```text
1 個主要任務
2 個次要任務
1 個必要警告
```

### 11.4 初期規則

Playable Prototype 只啟用依賴 Tier A 或已驗證 Tier B 的規則：

```text
R001 PopulationLow
R002 PopulationCritical
R003 SustainedWoodOverflow
R004 CastleResourcesReady
R005 VisionUnstable
```

暫不啟用：

- 第 3 座 TC 建議。
- TC 停村判定。
- 村民目標與停止生村。
- 威脅、防守與文明規則。

這些規則等相應資料來源通過驗收後再加入。

### 11.5 規則判斷原則

規則逐步從靜態門檻升級為：

```text
發展階段
+ 資源趨勢
+ 持續時間
+ Player Profile
+ 欄位信心
→ Recommendation
```

早期門檻只是可測試的基準，不視為完整 AOE2 策略模型。

---

## 12. Farmer Mode

Farmer Mode 是第一個也是 Alpha 前唯一的 Coach Profile。

```text
經濟      40%
安全      25%
科技      15%
人口管理  10%
軍事      10%
```

行為原則：

- 經濟穩定優先。
- 允許較晚升時代。
- 不主動催促 Rush。
- 用「可以考慮」而非命令式文案。
- 資料不足時保持安靜。

Balanced、Turtle 與 Custom Mode 都列入 Roadmap，不進入 Alpha 範圍。

---

## 13. Overlay UX

### 13.1 目標

- 小、不擋遊戲。
- Always on Top。
- 可拖曳。
- 支援 Click-through。
- 半透明。
- 可立即關閉。

### 13.2 Playable Prototype 畫面

```text
┌─ AgePilot ─────────────────┐
│ 🌾 Farmer                  │
│                            │
│ 💡 下一步                  │
│ 人口空間不多，可以準備房屋 │
│                            │
│ 🪵 木材持續增加            │
│ 可以考慮增加一些農田       │
└────────────────────────────┘
```

### 13.3 顯示模式

Playable Prototype 只需要：

```text
Off
Compact
```

Minimal、Full、完整 Dashboard 與多頁設定留到 Alpha 或其後。

---

## 14. 系統架構

### 14.1 邏輯資料流

```text
AOE2 Window
→ Capture
→ Vision
→ Observation Validation
→ GameState Estimator
→ History
→ Rule Engine
→ Recommendation Coordinator
→ Overlay
```

### 14.2 Solution 結構

早期避免過度拆分，建議先採用：

```text
AgePilot.sln

src/
  AgePilot.App
  AgePilot.Core
  AgePilot.Vision
  AgePilot.Infrastructure

tests/
  AgePilot.Core.Tests
  AgePilot.Vision.Tests
  AgePilot.IntegrationTests

testdata/
  screenshots/
```

職責穩定後，再視需要抽出 `Capture`、`Engine`、`Overlay`、`Diagnostics` 專案。避免在 Prototype 階段先支付八個專案的維護成本。

### 14.3 模組責任

**AgePilot.App**

- WPF lifecycle、DI、視窗與 Overlay。
- 啟動、停止與狀態呈現。

**AgePilot.Core**

- Observation、GameState、History。
- 規則、Profile 與 Recommendation。
- 不依賴 WPF 或具體 OCR 實作。

**AgePilot.Vision**

- 視窗偵測、Capture abstraction。
- ROI、前處理、OCR、辨識器與驗證。

**AgePilot.Infrastructure**

- 設定、日誌及未來的 SQLite。

---

## 15. 儲存、日誌與診斷

### 15.1 Prototype

- 設定先使用 JSON。
- 遊戲狀態保留在記憶體。
- 使用結構化本機日誌。
- 不需要 SQLite。

### 15.2 Alpha

只有在需要 Session history 時加入 SQLite：

```text
GameSessions
GameSnapshots
RecommendationEvents
RuleEvents
```

### 15.3 Vision Debugger

Vision Debugger 是開發必要工具，不等同完整產品 Dashboard。至少顯示：

- 原始畫面與 ROI。
- 前處理結果。
- OCR 原始文字與解析值。
- Confidence、Status 與最近可信值。
- 規則為何被允許或抑制。

---

## 16. 效能與穩定性目標

初始目標：

```text
CPU average       < 5%
Memory            < 300 MB
Capture FPS       5
OCR latency       < 300 ms
Suggestion delay  < 2 sec
```

驗收必須同時觀察 AOE2 幀率，不能只測 AgePilot 自身指標。

必測狀態：

- 遊戲未啟動。
- 遊戲載入中。
- 正常遊玩。
- 暫停與 Alt+Tab。
- 最小化、改解析度及切換螢幕。
- 遊戲結束或程序意外關閉。

狀態模型：

```text
GameNotFound
GameDetected
GameLoading
GameActive
GamePaused
GameUnavailable
GameEnded
```

---

## 17. 開發階段與 Gate

### Phase 0 — Vision Spike

目的：證明固定環境下可可靠讀取 Tier A 資料。

範圍：

- 偵測 AOE2 視窗。
- 擷取畫面。
- 手動 ROI。
- 食物、木材、黃金、石頭與人口辨識。
- Confidence、驗證及時間確認。
- Console 或簡單 Live Debug Panel。
- 截圖資料集與自動回歸測試。

不做：

- 正式 Overlay。
- SQLite。
- 完整 Dashboard。
- 多 Profile。
- TC、村民或 Threat Detection。

完成條件：通過第 10.3 節 Vision Gate。

### Phase 1 — Playable Prototype

目的：證明少量建議能在完整對局中穩定運作。

範圍：

- GameState 與短期 History。
- Farmer Mode。
- 3～5 條可靠規則。
- Rule priority、cooldown、suppression。
- Compact Overlay。
- JSON 設定與本機日誌。
- Vision / Rule Debug View。

完成條件：

- 連續完成至少五局實機測試且不 Crash。
- 沒有低信心資料直接觸發建議。
- 每局錯誤提示與洗版事件均有記錄。
- 使用者能開啟 App、進入遊戲並自動得到建議。
- 對 AOE2 的效能影響在既定門檻內。

### Phase 2 — Alpha

目的：形成可交付給外部測試者的 Windows 應用程式。

範圍：

- 1440p／HUD 50% 校正環境穩定支援。
- 視驗證結果加入 1080p 或其他 HUD Scale。
- 基本 Dashboard 與設定。
- Session persistence 與 SQLite。
- Installer 或 Portable package。
- Privacy、Disclaimer、README。
- 診斷資料匯出。

完成條件：

- 全新 Windows 環境可安裝並啟動。
- 支援環境與限制有清楚說明。
- 內部測試資料顯示錯誤提示率可接受。
- 使用者可停用 Overlay、紀錄與診斷輸出。

### Phase 3 — Economy Coach

- 趨勢式規則。
- 村民與 TC 辨識研究。
- TC idle 推論。
- 資源曲線與人口封頂分析。
- Post-game economy report。

### Phase 4 — Defense and Civilization

- MiniMap 與 Threat Detection。
- 防守規則。
- 文明辨識與文明 Profile。
- Balanced / Turtle Mode。

### Phase 5 — Contextual Coach

- 玩家習慣分析。
- 語音提示。
- 可選 LLM 文案改寫與賽後摘要。
- 自訂玩法 Profile。

LLM 永遠不應成為即時核心決策的必要依賴。

---

## 18. 近期里程碑

### v0.1 — First Sight

- Detect game。
- Capture HUD。
- Read resources and population。
- Confidence 與 Live Debug Panel。
- Screenshot regression tests。

### v0.2 — First Advice

- GameState 與短期 History。
- Farmer Mode。
- 3～5 條 Tier A/B 規則。
- Recommendation Debug UI。

### v0.3 — First Drive

- Compact Overlay。
- Click-through 與 Hotkey。
- Cooldown、suppression 與 suggestion lifecycle。
- 五局以上完整實機測試。

### v0.4 — Alpha Foundation

- Dashboard 與設定。
- SQLite Sessions。
- Packaging、Privacy 與診斷匯出。

### v0.5 — Public Alpha

- 經驗證的解析度支援。
- Farmer Mode 可供外部測試。
- 已知限制、安裝文件與問題回報流程。

---

## 19. Backlog 優先順序

### P0：Vision Gate

- [ ] 偵測 AOE2 視窗與生命週期。
- [ ] 擷取固定支援環境畫面。
- [ ] 定義 HUD Profile 與 ROI。
- [ ] 辨識四項資源與人口。
- [ ] 建立 Confidence 與 validation pipeline。
- [ ] 建立 screenshot metadata 格式。
- [ ] 建立 Vision regression tests。
- [ ] 測量 Vision Gate 指標。

### P1：Playable Prototype

- [ ] 建立 Observation 與 GameState。
- [ ] 建立短期時間序列 History。
- [ ] 建立 Rule requirements 與 Rule engine。
- [ ] 實作第一批 3～5 條規則。
- [ ] 實作 cooldown、suppression 與 lifecycle。
- [ ] 建立 Compact Overlay。
- [ ] 建立 Vision / Rule Debug View。
- [ ] 執行五局完整實機測試。

### P2：Alpha

- [ ] Dashboard 與設定頁。
- [ ] Session persistence 與 SQLite。
- [ ] 1440p Profile 驗證。
- [ ] 打包與安裝測試。
- [ ] Privacy、Disclaimer 與 README。
- [ ] 建立匿名問題回報所需的診斷匯出。

### P3：後續研究

- [ ] 村民與 TC 辨識。
- [ ] MiniMap / Threat Detection。
- [ ] 文明 Profile。
- [ ] Post-game report。
- [ ] Voice 與 Optional LLM。

---

## 20. 測試策略

### 20.1 Unit Tests

- 數字解析與合法範圍。
- Observation validation。
- Temporal confirmation。
- 規則輸入、輸出及抑制條件。
- Recommendation lifecycle。

### 20.2 Vision Regression Tests

- 每次 OCR 或前處理修改都跑完整截圖資料集。
- 測試結果輸出各欄位混淆案例。
- 指標退步時 CI 失敗或要求明確核准基準變更。

### 20.3 Replay Tests

將已錄製的影格序列餵入完整 pipeline，驗證：

- GameState 時序。
- 誤讀恢復時間。
- Recommendation 數量與時間點。
- 是否產生洗版或錯誤提示。

### 20.4 實機測試

每次里程碑記錄：

- 遊戲與 AgePilot 是否 Crash。
- OCR 不可用時間。
- 錯誤提示與遺漏提示。
- 提示被忽略或關閉的原因。
- CPU、記憶體及對遊戲 FPS 的影響。

---

## 21. 產品成效指標

不以競技勝率或 APM 作為主要指標。

優先追蹤：

```text
OCR exact accuracy
High-confidence OCR error rate
False recommendation rate
Population block duration
Resource overflow duration
Suggestion dismiss rate
Repeated suggestion count
Crash-free sessions
Game FPS impact
```

指標必須先能由可靠資料計算；無法觀察的指標不應假裝精準。

---

## 22. 風險與緩解措施

### 22.1 OCR 與 HUD 變異

風險：解析度、UI Scale、HDR、語言、動畫與遊戲更新造成辨識失效。

緩解：

- 固定第一個支援矩陣。
- HUD Profile 版本化。
- Screenshot regression tests。
- Fail closed 與辨識健康狀態。
- 通過固定環境後才研究 Anchor-based calibration。

### 22.2 錯誤建議

風險：錯誤建議會快速破壞信任。

緩解：

- 欄位級 Confidence 與 freshness。
- 多幀確認與最短持續時間。
- 規則宣告資料依賴。
- 低信心規則不顯示。
- Replay 與實機記錄 `FalseRecommendationRate`。

### 22.3 範圍膨脹

風險：在 Vision 未成熟前投入 Dashboard、AI 或多模式。

緩解：

- 以 Gate 而不是日期決定進入下一階段。
- Alpha 前只有 Farmer Mode。
- 新功能必須說明依賴哪個可靠資料層級。

### 22.4 遊戲效能與視窗狀態

風險：畫面擷取或 OCR 造成掉幀，或在最小化、全螢幕模式下失效。

緩解：

- 限制 Capture/OCR 頻率。
- 將每種視窗狀態列入測試矩陣。
- Capture、Vision 與 Overlay 各自提供健康狀態。

### 22.5 公平性與政策變化

風險：即使技術上只讀，公開發佈前仍需確認遊戲服務條款及相關政策是否允許所提供的功能。

緩解：

- 保持畫面觀察、無輸入、無注入的邊界。
- 公開版本前完成政策檢查並留下日期與來源。
- 清楚揭露功能與限制，不宣稱官方認可。

---

## 23. 發佈與隱私

Alpha 優先提供：

```text
Self-contained
win-x64
Portable 或 Installer
```

公開版本必須包含：

- 支援的解析度、UI Scale、語言及視窗模式。
- Privacy 說明。
- Read-only 技術邊界。
- 已知問題與診斷匯出方式。
- 第三方元件授權。
- 非官方工具聲明。

Auto Update、Stable/Beta channel 與 Telemetry 不屬於首個 Alpha 的必要條件。

---

## 24. 決策紀錄

已確定：

- Windows-first。
- C# / .NET 8 / WPF。
- 本機畫面辨識。
- 確定性規則為核心。
- Farmer Mode 為第一個 Profile。
- Vision Gate 優先於產品 UI。
- Prototype 不使用 SQLite。
- 初始校正環境為 2560×1440、全螢幕、繁中、HUD 50%。
- 標準化 ROI 只保證相同 HUD 佈局的比例換算；任意配置需要 Anchor 校正證據。

待驗證：

- OCR 引擎與前處理組合。
- Windows Graphics Capture 在各視窗模式的相容性。
- 時代辨識的可靠方式。
- 1440p 是否納入 Public Alpha。
- WPF Overlay 對遊戲效能的實際影響。
- Public Alpha 前的遊戲政策相容性。

---

## 25. 下一步

立即執行的工作不是建立完整 WPF Dashboard，而是撰寫並完成 Phase 0 技術實驗：

```text
1. 定義唯一支援測試環境
2. 擷取 AOE2 視窗
3. 建立手動 ROI Profile
4. 先辨識 Food
5. 擴充至 Wood / Gold / Stone / Population
6. 建立 metadata 與 regression tests
7. 加入 Confidence、validation 與 temporal confirmation
8. 測量 Vision Gate 指標
9. 依結果決定進入 Playable Prototype 或繼續改善 Vision
```

只有 Vision Gate 通過後，才開始正式的 Farmer Rules 與 Overlay 開發。

---

## 26. 最終願景

長期架構仍是：

```text
Vision
+ State
+ History
+ Rules
+ Player Style
→ Contextual Coach
```

但產品必須從可靠的小範圍開始。AgePilot 的價值不在於提示最多，而在於：

> **它知道什麼時候有足夠把握開口，也知道什麼時候應該保持安靜。**
