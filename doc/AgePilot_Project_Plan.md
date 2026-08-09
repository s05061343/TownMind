# AgePilot 專案計劃書

> **A calm companion for Age of Empires II**  
> 一個以「休閒、種田、穩健發展」為核心的《Age of Empires II: Definitive Edition》即時輔助教練。

---

## 1. 專案摘要

### 1.1 專案名稱

**AgePilot**

### 1.2 專案定位

AgePilot 是一個針對《Age of Empires II: Definitive Edition》（以下簡稱 AOE2 DE）的桌面輔助 App。

它不直接操作遊戲、不注入遊戲程序，也不修改遊戲資料，而是透過：

- 遊戲視窗畫面擷取
- OCR / 圖像辨識
- 遊戲狀態推論
- 規則引擎
- 桌面 Overlay

即時分析玩家目前的發展狀態，並以簡潔、不打擾遊戲節奏的方式，提示下一步適合進行的行動。

AgePilot 的第一個主要使用情境不是競技天梯，而是：

> **對戰 AI、休閒遊玩、偏好種田與慢節奏發展的玩家。**

---

## 2. 核心理念

一般 AOE2 Build Order 工具多半強調：

- 上時代速度
- 極限資源配置
- Rush Timing
- 開局秒數
- 軍事壓制
- 高 APM

AgePilot 採取不同方向。

核心優先順序：

```text
經濟穩定
  ↓
基地安全
  ↓
人口與資源平衡
  ↓
科技發展
  ↓
軍事準備
  ↓
主動進攻
```

對「農夫模式」而言，AgePilot 不會因玩家比標準 Build Order 慢 30 秒就大量警告。

它更在意：

- TC 是否長時間停村
- 人口是否即將卡住
- 木材是否大量囤積卻沒有轉成農田
- 黃金是否不足以支撐升級
- 是否已經有足夠資源補 TC
- 經濟人口是否不足
- 敵人來襲時是否需要暫停擴張
- 帝王時代是否該停止增加村民

AgePilot 的角色應該像：

> **副駕駛，而不是教官。**

---

# 3. 專案目標

## 3.1 MVP 目標

第一版必須完成：

1. 自動偵測 AOE2 DE 遊戲視窗。
2. 擷取指定遊戲畫面區域。
3. OCR 辨識：
   - 食物
   - 木材
   - 黃金
   - 石頭
   - 人口
   - 人口上限
   - 遊戲時間
4. 推定目前時代。
5. 建立玩家目前遊戲狀態。
6. 規則引擎根據狀態產生建議。
7. 透過透明 Overlay 顯示：
   - 目前狀態
   - 下一步
   - 警告
8. 提供「農夫模式」。
9. 設定內容可保存。
10. 完全不需修改遊戲檔案。

---

## 3.2 中期目標

第二階段增加：

- 村民數量推估
- TC 數量辨識
- 建築辨識
- 軍事人口估計
- 經濟分配建議
- 文明辨識
- 不同文明策略
- 基地遭受攻擊偵測
- 敵方兵種粗略辨識
- 防守建議
- 玩家行為歷史紀錄

---

## 3.3 長期目標

AgePilot 最終可以發展成：

```text
AOE2 畫面
   ↓
Vision Layer
   ↓
Game State Model
   ↓
Strategy Engine
   ↓
Player Profile
   ↓
Contextual Coach
   ↓
Overlay / Voice / History
```

長期功能：

- 即時語音提示
- AI 戰局摘要
- 每局遊戲結束後復盤
- 玩家習慣分析
- 自訂 Build Style
- Mod / 地圖差異配置
- 多螢幕 Dashboard
- 歷史趨勢統計
- 不同遊戲速度自適應
- 不同解析度自動校正

---

# 4. 使用者族群

## 4.1 第一優先

### Casual AI Player

特徵：

- 主要打 AI
- 不追求 Rank
- 喜歡基地經營
- 喜歡慢慢發展
- 不喜歡極限 Build Order
- 希望有人提醒但不想一直被催

---

## 4.2 第二優先

### 新手玩家

主要問題：

- 不知道現在應該做什麼
- 常卡人口
- 資源配置失衡
- 忘記升科技
- 忘記生村
- 不知道何時上時代
- 不知道何時停止種田

AgePilot 可以把遊戲中的大量決策簡化成：

```text
現在
↓
下一步
↓
為什麼
```

---

## 4.3 第三優先

### Returning Player

適合很久沒玩 AOE2、但知道基本操作的人。

AgePilot 不教：

- 如何移動單位
- 如何蓋建築
- 基礎 UI

而是幫忙恢復：

- 經濟節奏
- 時代轉換
- 人口配置
- 中後期節奏

---

# 5. 非目標

第一階段明確不做：

- 自動滑鼠操作
- 自動鍵盤輸入
- 自動生產單位
- 自動控制軍隊
- 遊戲進程注入
- 讀取遊戲記憶體
- DLL Injection
- 修改遊戲封包
- 自動打仗
- 自動 Build Order Bot

AgePilot 應維持：

> **Read-only / Observation-only Assistant**

---

# 6. 產品模式

AgePilot 提供多種 Coach Profile。

---

## 6.1 Farmer Mode 🌾

預設模式。

權重：

```text
經濟      40%
安全      25%
科技      15%
人口管理  10%
軍事      10%
```

特性：

- 鼓勵 3 TC
- 鼓勵大量農田
- 允許較晚升時代
- 防守優先
- 不鼓勵早期 Rush
- 中後期資源充裕後才進攻

---

## 6.2 Balanced Mode ⚖

未來加入。

```text
經濟 30%
軍事 25%
安全 20%
科技 15%
人口 10%
```

---

## 6.3 Turtle Mode 🏰

偏防守。

策略：

- 城牆
- 城堡
- 塔
- chokepoint
- 內部農田
- 少量反制兵
- 帝王時代反攻

---

## 6.4 Custom Mode

允許玩家設定：

```yaml
economy: 40
defense: 25
military: 10
technology: 15
population: 10
```

---

# 7. 核心使用流程

啟動：

```text
AgePilot
   ↓
尋找 AOE2 DE 視窗
   ↓
確認遊戲解析度
   ↓
載入 HUD Profile
   ↓
開始 Screen Capture
   ↓
OCR
   ↓
建立 GameState
   ↓
Rule Engine
   ↓
Recommendation
   ↓
Overlay
```

---

# 8. 系統架構

建議採用：

**.NET 8 + WPF**

Solution：

```text
AgePilot.sln

src/
 ├─ AgePilot.App
 ├─ AgePilot.Core
 ├─ AgePilot.Capture
 ├─ AgePilot.Vision
 ├─ AgePilot.Engine
 ├─ AgePilot.Overlay
 ├─ AgePilot.Infrastructure
 └─ AgePilot.Diagnostics

tests/
 ├─ AgePilot.Core.Tests
 ├─ AgePilot.Vision.Tests
 └─ AgePilot.Engine.Tests
```

---

# 9. 模組說明

## 9.1 AgePilot.App

主要責任：

- DI
- Lifecycle
- Settings
- App startup
- Tray icon
- Window management

---

## 9.2 AgePilot.Capture

負責：

- 找到 AOE2 DE Window Handle
- Windows Graphics Capture
- Frame Buffer
- FPS 控制
- ROI 裁切

主要介面：

```csharp
public interface IGameCaptureService
{
    bool IsGameDetected { get; }

    Task StartAsync();

    Task StopAsync();

    event EventHandler<GameFrame> FrameCaptured;
}
```

---

## 9.3 AgePilot.Vision

負責：

- OCR
- HUD Detection
- Number Parsing
- Age Detection
- Icon Detection
- Confidence

Pipeline：

```text
Frame
 ↓
Crop
 ↓
Preprocess
 ↓
OCR
 ↓
Normalize
 ↓
Validate
 ↓
GameObservation
```

---

## 9.4 AgePilot.Core

Domain Models。

例如：

```csharp
public sealed class GameState
{
    public TimeSpan GameTime { get; init; }

    public GameAge Age { get; init; }

    public int Food { get; init; }

    public int Wood { get; init; }

    public int Gold { get; init; }

    public int Stone { get; init; }

    public int Population { get; init; }

    public int PopulationCap { get; init; }

    public int? Villagers { get; init; }

    public int? TownCenters { get; init; }

    public double Confidence { get; init; }
}
```

---

# 10. GameState 設計

完整 GameState 建議：

```text
GameState

General
 ├─ GameTime
 ├─ GameSpeed
 ├─ Age
 ├─ Civilization

Resources
 ├─ Food
 ├─ Wood
 ├─ Gold
 └─ Stone

Population
 ├─ Current
 ├─ Limit
 ├─ Villagers
 ├─ Military
 └─ IdleVillagers

Economy
 ├─ Farms
 ├─ Lumberjacks
 ├─ Farmers
 ├─ GoldMiners
 └─ StoneMiners

Buildings
 ├─ TownCenters
 ├─ Houses
 ├─ Markets
 ├─ Castles
 └─ MilitaryBuildings

Threat
 ├─ UnderAttack
 ├─ EnemyDensity
 └─ ThreatLevel
```

欄位可以逐步增加。

---

# 11. OCR 策略

## 11.1 不辨識整個畫面

只辨識 HUD 固定區域。

例如：

```text
┌─────────────────────────────────────┐
│ Food Wood Gold Stone          Pop   │
│                                     │
│                                     │
│              WORLD                  │
│                                     │
│                                     │
│ Command / Unit / Age                │
└─────────────────────────────────────┘
```

---

## 11.2 ROI

每種解析度建立 HUD Profile：

```json
{
  "resolution": "1920x1080",
  "food":   [10, 5, 90, 35],
  "wood":   [110, 5, 90, 35],
  "gold":   [210, 5, 90, 35],
  "stone":  [310, 5, 90, 35],
  "pop":    [1700, 5, 180, 40]
}
```

實際座標後續由畫面校正取得。

---

# 12. OCR 更新頻率

不需要 60 FPS。

建議：

```text
Capture       5 FPS
OCR           2 FPS
Rule Engine   1 FPS
Overlay       event driven
```

原因：

資源與人口並不需要毫秒級更新。

降低 CPU/GPU 使用率比極低延遲更重要。

---

# 13. OCR Confidence

每一個 observation 都要帶 Confidence。

例如：

```csharp
public sealed record ObservedValue<T>(
    T Value,
    double Confidence,
    DateTime Timestamp);
```

如果：

```text
Food = 812
confidence = 0.95
```

可採用。

如果：

```text
Gold = 388
confidence = 0.43
```

則保留上一筆可信值。

---

# 14. Temporal Smoothing

OCR 不應該直接覆蓋 GameState。

例如：

```text
Frame 1: Food = 812
Frame 2: Food = 872
Frame 3: Food = 82   ← OCR 錯誤
Frame 4: Food = 901
```

系統需要判斷：

```text
82 不合理
```

採用：

- Median Filter
- Maximum delta
- Confidence
- previous value
- temporal window

---

# 15. 規則引擎

AgePilot 的第一版智慧核心不是 LLM。

而是：

> **Deterministic Rule Engine**

---

## 15.1 Rule Interface

```csharp
public interface ICoachRule
{
    string Id { get; }

    RuleResult Evaluate(
        GameState state,
        GameHistory history,
        PlayerProfile profile);
}
```

---

# 16. Recommendation

```csharp
public sealed record Recommendation(
    string Id,
    CoachSeverity Severity,
    string Title,
    string Description,
    string Category,
    int Priority,
    TimeSpan Cooldown);
```

Severity：

```text
Info
Suggestion
Warning
Critical
```

---

# 17. Farmer Mode 規則

## 17.1 Housing

```text
IF
PopulationCap - Population <= 5

THEN
準備房屋
```

提示：

> 人口上限只剩 5，現在先安排一位村民蓋房。

---

## 17.2 Wood Overflow

```text
IF
Wood > 700
AND Age >= Feudal
AND Food < 800

THEN
增加農田
```

提示：

> 木材已經有些囤積，可以再鋪一批農田，把木材轉成食物收入。

---

## 17.3 Castle Preparation

```text
IF
Age == Feudal
AND Food >= 600
AND Gold < 180

THEN
增加採金人口
```

提示：

> 食物已接近上城需求，黃金稍微不足，可以補幾位村民採金。

---

## 17.4 Castle Ready

```text
IF
Age == Feudal
AND Food >= 800
AND Gold >= 200

THEN
可以升城堡
```

Farmer Mode 文案：

> 經濟已經足夠舒服，可以考慮升城堡時代。

注意：

不是：

> 立即升城！

---

## 17.5 3 TC

```text
IF
Age == Castle
AND TownCenters < 3
AND Wood >= 550
AND Villagers < 100
AND ThreatLevel < High

THEN
補 TC
```

---

## 17.6 Villager Growth

```text
IF
Age == Castle
AND Villagers < 100

THEN
保持 TC 生村
```

---

## 17.7 Stop Villagers

```text
IF
Age == Imperial
AND Villagers >= 125

THEN
逐步停止生村
```

提示：

> 經濟人口已經相當完整，可以開始把後續人口留給軍隊。

---

# 18. Rule Priority

建議：

```text
Critical
 ↓
Safety
 ↓
Population
 ↓
Age Progress
 ↓
Economy
 ↓
Technology
 ↓
Military
```

如果同時發生：

```text
人口快滿
+
木材過多
+
可以補 TC
```

Overlay 不應該同時顯示 10 條。

只顯示：

```text
1 個主要任務
2 個次要任務
1 個警告
```

---

# 19. 防洗版策略

Coach 最大的 UX 問題不是「提示不夠」。

而是：

> **提示太多。**

因此每條 Rule 要有：

- Cooldown
- Minimum active duration
- Dismiss support
- acknowledgement
- priority suppression

例如：

```text
BuildHouse
Cooldown = 45 seconds
```

不能每秒：

```text
蓋房
蓋房
蓋房
蓋房
```

---

# 20. Overlay UI

目標：

- 小
- 不擋遊戲
- 可拖曳
- Click-through
- 半透明
- Always on Top

---

## 20.1 Compact Mode

```text
┌─ AgePilot ─────────────────┐
│ 🌾 Castle Age              │
│                            │
│ 💡 下一步                  │
│ 建造第 3 座 TC             │
│                            │
│ 🪵 木材稍多                │
│ 可以再鋪 6~8 塊農田        │
└────────────────────────────┘
```

---

## 20.2 Expanded Mode

```text
┌─ AgePilot ──────────────────────┐
│ 🌾 Farmer Mode             ●   │
│                                │
│ Castle Age             31:42   │
│                                │
│ 🍖 1280      🪵 682            │
│ 🪙  310      🪨 226            │
│                                │
│ 👤 83 / 100                    │
│                                │
│ ────────────────────────────── │
│ 下一步                         │
│                                │
│ ① 建造第 3 座 TC               │
│ ② 再增加 6~8 塊農田            │
│ ③ 保持 TC 生產村民             │
│                                │
│ 狀態：安全                     │
└────────────────────────────────┘
```

---

# 21. Overlay 顯示模式

支援：

```text
Off
Minimal
Compact
Full
```

Hotkey：

```text
Ctrl + Shift + A
```

切換顯示。

---

# 22. 主控制台

除了 Overlay，AgePilot 還需要 Dashboard。

頁面：

```text
Dashboard
Live
Profiles
Rules
History
Vision
Settings
About
```

---

# 23. Dashboard

顯示：

- Game detected
- Capture status
- Current mode
- Resolution
- OCR health
- Last session
- Start / Stop coach

---

# 24. Vision Debugger

這會是非常重要的開發工具。

顯示：

```text
Screenshot
+
ROI Rectangle
+
OCR Result
+
Confidence
```

例如：

```text
Food ROI

[ image ]

OCR: 1280
Confidence: 96%
```

這會大幅降低 Debug OCR 的痛苦。

---

# 25. Rule Debugger

顯示：

```text
HousingRule
Matched: false

WoodOverflowRule
Matched: true
Priority: 70

CastleTransitionRule
Matched: false
```

這讓規則可以快速調整。

---

# 26. GameHistory

不只保存現在狀態。

建議：

```csharp
public sealed class GameSnapshot
{
    public DateTime Timestamp { get; init; }

    public GameState State { get; init; }
}
```

每：

```text
5 秒
```

保存一次 Snapshot。

---

# 27. History 的價值

可以推算：

### TC Idle

如果：

```text
村民數 2 分鐘沒增加
```

且：

```text
有 TC
```

可推測：

> TC 可能停止生產。

---

### Resource Trend

例如：

```text
Wood

5 min ago  250
4 min ago  400
3 min ago  560
2 min ago  720
1 min ago  910
```

代表：

> 木材持續累積。

比單純判斷：

```text
Wood > 800
```

更準確。

---

# 28. State Machine

AgePilot 可以建立發展階段：

```text
DarkAgeGrowth
 ↓
FeudalSetup
 ↓
CastlePreparation
 ↓
CastleBoom
 ↓
ImperialPreparation
 ↓
ImperialEconomy
 ↓
ArmyTransition
 ↓
LateGame
```

這比單純 Age enum 更適合產生建議。

---

# 29. Farmer Mode State Flow

```text
Dark Age

22~28 villagers
 ↓
Feudal

基本防禦
經濟科技
農田
 ↓
800 Food
200 Gold
 ↓
Castle

2~3 TC
 ↓
70 Villagers
 ↓
100 Villagers
 ↓
安全則繼續 Boom
 ↓
Imperial
 ↓
120~130 Villagers
 ↓
停止 Boom
 ↓
軍事人口增加
 ↓
大軍出征
```

---

# 30. Threat Detection

MVP 可以先沒有。

V2 加入。

第一版 Threat Detection 不需要精準辨識所有單位。

可以先判斷：

```text
敵方顏色像素
+
MiniMap Density
+
畫面紅色輪廓
+
受攻擊 UI
```

輸出：

```text
None
Low
Medium
High
Critical
```

---

# 31. Threat Rules

例如：

```text
IF ThreatLevel >= High
AND Age == Castle

THEN
Suppress Eco Expansion
```

暫時抑制：

- 補第 3 TC
- 大量農田
- 上帝王

改成：

- 補軍事
- 防守
- 城堡
- 牆
- 避免資源斷線

---

# 32. 文明系統

V2 開始。

模型：

```csharp
public sealed class CivilizationProfile
{
    public string Id { get; init; }

    public string Name { get; init; }

    public EconomyPreference Economy { get; init; }

    public IReadOnlyList<string> PreferredUnits { get; init; }

    public IReadOnlyList<RuleModifier> RuleModifiers { get; init; }
}
```

---

# 33. 文明差異

例如高棉：

```text
FarmWeight +20%
FoodTransition +10%
```

條頓：

```text
DefenseWeight +15%
FarmWeight +10%
```

拜占庭：

```text
DefenseWeight +25%
CounterUnitWeight +20%
```

---

# 34. LLM 的角色

LLM 不應該放在主要決策 Loop。

架構：

```text
Rule Engine
   ↓
Structured Recommendation
   ↓
Optional LLM
   ↓
Natural Language
```

例如 Engine：

```json
{
  "action": "AddFarm",
  "amount": 8,
  "reason": "WoodOverflow",
  "priority": 65
}
```

LLM 可以改寫成：

> 木材開始有點多了，現在很適合再鋪 6～8 塊農田，先把經濟轉成食物。

即使沒有 LLM，AgePilot 仍然可以完全正常運作。

---

# 35. Voice Coach

後期功能。

設定：

```text
Voice Coach

[ ] Enabled

Minimum Severity:
Suggestion / Warning / Critical

Cooldown:
60 sec
```

語音最好只念：

- 人口卡住
- 被攻擊
- 資源嚴重失衡
- 可以升時代
- 帝王轉軍事

避免一直講話。

---

# 36. 設定檔

建議：

```json
{
  "profile": "farmer",
  "overlay": {
    "mode": "compact",
    "opacity": 0.85
  },
  "capture": {
    "fps": 5
  },
  "coach": {
    "maxSuggestions": 3,
    "cooldownSeconds": 30
  }
}
```

---

# 37. Rule Config

部分規則不要寫死。

例如：

```yaml
farmer:
  feudal:
    castleFoodTarget: 800
    castleGoldTarget: 200

  castle:
    desiredTownCenters: 3
    villagerTarget: 105

  imperial:
    villagerTarget: 125
```

---

# 38. 儲存

MVP：

**SQLite**

資料：

```text
Settings
GameSessions
GameSnapshots
Recommendations
RuleEvents
```

不需要外部 DB。

---

# 39. Database Schema

```text
GameSession
-----------
Id
StartedAt
EndedAt
Profile
Civilization
Map
Result

GameSnapshot
------------
Id
SessionId
Timestamp
Age
Food
Wood
Gold
Stone
Population
PopulationCap
Villagers

RecommendationEvent
-------------------
Id
SessionId
Timestamp
RuleId
Severity
Message
Accepted
```

---

# 40. 日誌

建議 Serilog。

輸出：

```text
logs/
  agepilot-2026-08-09.log
```

Level：

```text
Information
Warning
Error
Debug
```

Vision Debug 可以單獨：

```text
logs/vision/
```

---

# 41. Telemetry

預設：

> Disabled

如果未來需要匿名 Telemetry：

一定明確 Opt-in。

只收：

- App version
- OCR error
- Resolution
- Crash

不要收：

- Screenshot
- 遊戲畫面
- 玩家名稱

除非玩家主動允許。

---

# 42. 效能目標

AgePilot 不能因為「輔助遊戲」本身讓遊戲掉幀。

目標：

```text
CPU average       < 5%
Memory            < 300 MB
Capture FPS       5
OCR latency       < 300ms
Suggestion delay  < 2 sec
```

Overlay：

```text
GPU usage minimal
```

---

# 43. Fault Tolerance

AOE2 可以：

- Alt+Tab
- 切全螢幕
- 改解析度
- 讀取畫面
- 暫停
- 結束遊戲

AgePilot 必須能處理。

State：

```text
GameNotFound
GameDetected
GameLoading
GameActive
GamePaused
GameEnded
```

---

# 44. Resolution Support

MVP 優先：

```text
1920x1080
2560x1440
```

V2：

```text
3440x1440
3840x2160
```

以及 UI Scale：

```text
100%
125%
150%
```

---

# 45. Auto Calibration

長期避免手動維護每個 Resolution Profile。

方法：

```text
搜尋 HUD Anchor Icon
 ↓
取得座標
 ↓
根據比例推算 Resources ROI
```

例如利用：

- Food icon
- Wood icon
- Population icon

做 Anchor。

---

# 46. 錯誤處理

如果 OCR Confidence 太低：

Overlay 顯示：

> ⚠ 畫面辨識不穩定

而不是給出錯誤建議。

規則：

```text
Confidence < 0.6

→ 不做 Economy Recommendation
```

---

# 47. Session Replay

V2。

GameSnapshot 可以回放：

```text
00:00
05:00
10:00
15:00
...
```

產生：

```text
Villager Growth
Resource Curve
Age Timing
Population Block
```

---

# 48. Post Game Report

長期非常有價值。

例如：

```text
AgePilot Session Summary

Game Length: 1:02:31

Dark → Feudal
14:22

Feudal → Castle
27:51

Castle → Imperial
48:10

Peak Villagers
128

Population Blocks
3

Longest Population Block
01:22

Major Resource Overflow
Wood: 1450

Suggested Improvement
城堡時代中段木材長期過量，
下一局可以更早增加農田。
```

這對休閒玩家比「APM」更有意義。

---

# 49. 安全與公平性原則

AgePilot 的設計原則：

```text
Screen Observation Only
```

不：

```text
Hook
Inject
Read Memory
Modify Network
Send Input
```

因此所有資訊原則上都是：

> 玩家在畫面上本來就能看到的資訊。

---

# 50. UX 原則

AgePilot 必須遵守：

### 1. 不催

不是：

> 你慢了 48 秒。

而是：

> 目前經濟穩定，可以準備下一個時代。

### 2. 不洗版

一次最多：

```text
3 suggestions
```

### 3. 不過度精準

不要求：

```text
現在派 2.3 個村民砍木
```

而是：

```text
增加約 3～5 位木工
```

### 4. 不取代玩家

玩家仍然決定：

> 我要不要照做。

---

# 51. UI Tone

AgePilot 的語氣：

```text
Calm
Helpful
Low Pressure
```

避免：

```text
WRONG!
TOO SLOW!
BAD ECONOMY!
```

採用：

```text
木材開始偏多，可以考慮增加一些農田。
```

---

# 52. 品牌

名稱：

# AgePilot

Tagline：

> **A calm companion for Age of Empires II**

中文：

> **陪你慢慢建立自己的帝國。**

---

# 53. Logo 方向

概念：

```text
盾牌
+
農田
+
羅盤 / Pilot
```

或者：

```text
城堡剪影
+
指南針指標
```

避免做得像：

- Cheat Engine
- Hacker tool
- FPS overlay

整體偏：

```text
Medieval
+
Modern UI
```

---

# 54. 色彩方向

Dark UI。

建議：

```text
Background
#151713

Panel
#20241D

Primary
#C6A15B

Success
#7FA66A

Warning
#D3A24C

Danger
#BF655D
```

視覺：

> 中世紀羊皮紙 × 現代 Dark Dashboard

---

# 55. 開發階段

---

## Phase 0 — Prototype

目標：

證明能從遊戲畫面可靠讀到數字。

內容：

- Window detect
- Screenshot
- Food OCR
- Wood OCR
- Gold OCR
- Stone OCR
- Population OCR

完成條件：

```text
1920x1080

Resource OCR accuracy
>= 95%
```

---

## Phase 1 — MVP

內容：

- WPF App
- Capture Service
- OCR
- GameState
- Rule Engine
- Farmer Mode
- Overlay
- Settings
- Logging
- Vision Debugger

可實際遊玩使用。

---

## Phase 2 — Economy Coach

加入：

- GameHistory
- Trend detection
- Villager estimation
- Town Center detection
- Better rules
- Session history
- Economy graph

---

## Phase 3 — Defense Coach

加入：

- MiniMap analysis
- Threat detection
- Under attack
- Defensive recommendations

---

## Phase 4 — Civilization Profiles

加入：

- Civ detection
- Civ bonuses
- Strategy profiles
- Preferred army

---

## Phase 5 — AI Coach

加入：

- Natural language explanation
- Post-game summary
- Player habit analysis

---

# 56. MVP Backlog

## Capture

- [ ] Detect AOE2 window
- [ ] Capture window
- [ ] FPS limiter
- [ ] Handle resize
- [ ] Handle minimized state

## Vision

- [ ] Crop Food
- [ ] Crop Wood
- [ ] Crop Gold
- [ ] Crop Stone
- [ ] Crop Population
- [ ] OCR parser
- [ ] confidence
- [ ] smoothing

## Core

- [ ] GameState
- [ ] GameSnapshot
- [ ] GameAge
- [ ] PlayerProfile

## Engine

- [ ] Rule interface
- [ ] Rule priority
- [ ] cooldown
- [ ] suppression
- [ ] Farmer rules

## UI

- [ ] Dashboard
- [ ] Overlay
- [ ] Compact mode
- [ ] Settings
- [ ] Debugger

## Persistence

- [ ] Settings JSON
- [ ] SQLite
- [ ] Session persistence

---

# 57. 第一批 Rule

建議 MVP 至少：

```text
R001 PopulationLow
R002 PopulationCritical
R003 WoodOverflow
R004 FoodLow
R005 GoldLowForCastle
R006 CastleReady
R007 CastleTownCenter
R008 CastleBoom
R009 ImperialReady
R010 VillagerTargetReached
R011 EconomyBalanced
R012 ResourceOverflow
```

---

# 58. Rule Example

```csharp
public sealed class WoodOverflowRule : ICoachRule
{
    public string Id => "R003";

    public RuleResult Evaluate(
        GameState state,
        GameHistory history,
        PlayerProfile profile)
    {
        if (state.Age < GameAge.Feudal)
            return RuleResult.None;

        if (state.Wood < 700)
            return RuleResult.None;

        if (state.Food >= state.Wood)
            return RuleResult.None;

        return RuleResult.Suggest(
            title: "木材開始偏多",
            description: "可以再鋪一批農田，把部分木材轉成持續的食物收入。",
            priority: 50);
    }
}
```

---

# 59. Rule Engine

Pseudo：

```csharp
var results = rules
    .Select(x => x.Evaluate(state, history, profile))
    .Where(x => x.IsMatch)
    .Where(x => !cooldown.IsActive(x.RuleId))
    .OrderByDescending(x => x.Priority)
    .Take(3)
    .ToList();
```

---

# 60. Suggestion Memory

避免：

```text
建農田
→ 玩家建了
→ 10 秒後又提示建農田
```

因此 Recommendation 需要：

```text
Created
Displayed
Acknowledged
Resolved
Expired
```

---

# 61. 測試策略

## Unit Test

Rule Engine：

```text
Given Wood = 900
Food = 300
Age = Castle

Expected:
WoodOverflowRule = true
```

---

## Vision Test

建立：

```text
testdata/screenshots/
```

保存不同：

- 地圖
- 時代
- 解析度
- 資源值
- UI scale

所有 OCR 更新都跑 Regression Test。

---

# 62. Screenshot Dataset

格式：

```text
screenshots/

1920x1080/
  dark/
  feudal/
  castle/
  imperial/

2560x1440/
```

Metadata：

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

---

# 63. CI

GitHub Actions：

```text
Build
Test
Package
```

Flow：

```text
push
 ↓
dotnet restore
 ↓
dotnet build
 ↓
dotnet test
 ↓
publish
```

---

# 64. Release

Windows：

```text
AgePilot.exe
```

優先：

```text
Self-contained
win-x64
Single file
```

避免使用者安裝 .NET Runtime。

---

# 65. Auto Update

MVP 之後。

可以考慮：

- Velopack
- GitHub Releases

Release Channel：

```text
Stable
Beta
```

---

# 66. Repository

建議：

```text
AgePilot/

README.md
LICENSE
CHANGELOG.md
CONTRIBUTING.md
docs/
src/
tests/
tools/
```

---

# 67. README 結構

```text
AgePilot

Screenshot

What is AgePilot?

Features

Farmer Mode

How It Works

Installation

Roadmap

Privacy

Disclaimer
```

---

# 68. License

如果開源：

推薦：

```text
MIT
```

如果之後考慮商業版：

可以保留核心閉源。

---

# 69. 隱私

AgePilot 不應預設上傳 Screenshot。

所有 Vision：

```text
Local Processing
```

Settings 頁面清楚標示：

> AgePilot does not upload your game screen.

---

# 70. 潛在技術風險

## OCR

最大風險：

- 字體 Anti-Aliasing
- UI scale
- HDR
- Resolution
- 顏色
- 動畫

解法：

- ROI
- threshold
- grayscale
- confidence
- smoothing
- profile calibration

---

## Window Capture

風險：

- Exclusive fullscreen
- minimized
- monitor switch
- DPI

需要逐一測試。

---

## False Recommendation

這是產品最大風險。

如果 OCR 讀錯：

```text
Food 1200
→ 120
```

Coach 可能給錯建議。

因此：

```text
Vision confidence
>
Rule confidence
>
Suggestion
```

必須分層。

---

# 71. Rule Confidence

Recommendation 也應該有 Confidence：

```text
High
Medium
Low
```

Low Confidence：

不顯示。

---

# 72. Debug Mode

開發階段 Hotkey：

```text
Ctrl + Shift + D
```

顯示：

```text
Food ROI
Wood ROI
Gold ROI
OCR text
confidence
Rule state
```

---

# 73. Metrics

衡量 AgePilot 好不好，不用競技勝率。

應使用：

```text
Population blocks / game
TC idle duration
Resource overflow duration
Time to target villager count
Suggestion dismiss rate
OCR accuracy
False alert count
```

---

# 74. MVP 成功標準

第一版成功條件：

玩家可以：

1. 打開 AgePilot。
2. 開 AOE2。
3. AgePilot 自動辨識。
4. 不需要手動輸入資源。
5. Overlay 每隔一段時間提供合理提示。
6. 不影響遊戲 FPS。
7. 一整局遊戲不 Crash。
8. 不會瘋狂洗提示。

---

# 75. 首次開發建議

第一個 Prototype **不要先做漂亮 UI**。

先做：

```text
Capture
 ↓
Crop
 ↓
OCR
 ↓
Console Output
```

Console：

```text
Food: 812   98%
Wood: 521   96%
Gold: 201   97%
Stone: 108  95%
Pop: 61/80  99%
```

只要這層穩了，後面的 App 才值得做。

---

# 76. Prototype 順序

### Step 1

抓到 AOE2 HWND。

### Step 2

截圖。

### Step 3

手動設定 ROI。

### Step 4

OCR Food。

### Step 5

擴展到 Wood / Gold / Stone。

### Step 6

Population。

### Step 7

Temporal smoothing。

### Step 8

GameState。

### Step 9

Rule Engine。

### Step 10

Overlay。

---

# 77. 第一版畫面

主程式：

```text
┌──────────────────────────────────────┐
│ AgePilot                             │
├──────────────────────────────────────┤
│                                      │
│ Game                                 │
│ Age of Empires II DE     Connected   │
│                                      │
│ Mode                                 │
│ 🌾 Farmer                            │
│                                      │
│ Vision                               │
│ OCR Health               97%         │
│                                      │
│ Overlay                              │
│ [ ON ]                               │
│                                      │
│              Open Live View          │
│                                      │
└──────────────────────────────────────┘
```

---

# 78. Live View

```text
Current State

Castle Age
31:22

Food   1022
Wood    655
Gold    296
Stone   180

Population
81 / 100

Suggestions

1. 建造第 3 座 TC
2. 木材開始偏多，可以增加農田
3. 保持 TC 持續生村
```

---

# 79. 最終產品願景

AgePilot 不應該變成：

> 「告訴你怎麼玩才是正確。」

而是：

> **理解你想怎麼玩，再幫你把這個玩法玩得更舒服。**

如果玩家喜歡：

```text
Rush
```

AgePilot 可以支援 Rush。

如果玩家喜歡：

```text
Boom
```

AgePilot 支援 Boom。

如果玩家喜歡：

```text
種田 40 分鐘
蓋完整城牆
最後 100 人大軍出去
```

那也完全合理。

---

# 80. AgePilot 的一句話

> **AgePilot watches your empire grow, quietly tells you what matters next, and leaves the decisions to you.**

中文：

> **AgePilot 看著你的帝國慢慢成長，只在需要時告訴你下一步，而決定永遠留給玩家。**

---

# 81. 建議開發優先級總結

```text
P0
Capture
OCR
GameState
Farmer Rules
Overlay

P1
History
Trend
Town Center
Villagers
Vision Debugger

P2
Threat
MiniMap
Civilization

P3
Voice
AI Coach
Post Game Analysis
```

---

# 82. 建議第一個 Milestone

## AgePilot v0.1 — First Sight

完成：

- Detect game
- Capture HUD
- Read resources
- Read population
- Live debug panel

---

# 83. 第二個 Milestone

## AgePilot v0.2 — First Advice

完成：

- GameState
- Farmer Profile
- 10+ Rules
- Recommendation UI

---

# 84. 第三個 Milestone

## AgePilot v0.3 — Overlay

完成：

- Transparent overlay
- Click-through
- Compact view
- Hotkey
- Cooldown

---

# 85. 第四個 Milestone

## AgePilot v0.4 — Memory

完成：

- SQLite
- Game sessions
- Resource history
- Trend-based rules

---

# 86. 第一個公開版本

## AgePilot v0.5 Alpha

條件：

- 支援 1080p
- 支援 1440p
- Farmer Mode
- Overlay
- Session
- Installer / Portable build
- 基本 README

此版本就已經具備公開測試價值。

---

# 87. 專案結論

AgePilot 最大的價值不是「知道 AOE2 的標準 Build Order」。

而是：

```text
Vision
+
State
+
History
+
Rules
+
Player Style
```

它知道：

> **這一局，你現在應該做什麼。**

同時保留 Age of Empires 最重要的樂趣：

> 自己建立、自己決定、自己發展自己的帝國。

---

**Project:** AgePilot  
**Initial Profile:** Farmer Mode 🌾  
**Platform:** Windows  
**Target Game:** Age of Empires II: Definitive Edition  
**Primary Stack:** C# / .NET 8 / WPF  
**Architecture:** Screen Capture → Vision → GameState → Rule Engine → Overlay  
**Design Principle:** Calm, Local, Read-only, Player-first
