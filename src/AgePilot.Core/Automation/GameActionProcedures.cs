using AgePilot.Core.Observations;

namespace AgePilot.Core.Automation;

public enum ProcedureStepKind
{
    /// <summary>對遊戲送出按鍵序列。</summary>
    GameKey,
    /// <summary>
    /// 點擊已校準的命令面板格位（HUD profile 的 CommandGridRegion）。
    /// Phase 1 沒有任何程序使用——實際鍵位顯示生產村民與升時代都有快捷鍵。
    /// 保留作為後備：若 Phase 2 遇到沒有快捷鍵的指令仍可走這條路徑。
    /// </summary>
    CommandGridClick,
    /// <summary>點擊相對遊戲視窗的固定 normalized 世界位置。</summary>
    WorldClick,
    /// <summary>等待遊戲完成 UI 轉場，例如選取後面板重繪。</summary>
    Delay,
}

public sealed record ProcedureStep(
    ProcedureStepKind Kind,
    IReadOnlyList<GameKeyChord>? Keys = null,
    int Row = 0,
    int Column = 0,
    int DelayMs = 0,
    double X = 0,
    double Y = 0);

public sealed record GameActionProcedure(
    GameActionKind Kind,
    IReadOnlyList<ProcedureStep> Steps,
    Postcondition Post,
    TimeSpan Timeout,
    string Description,
    IReadOnlyList<ResourceCost> Costs);

/// <summary>
/// 具名動作 → 寫死程序的對應表（ADR 0015）。
///
/// 這不是第二個策略來源：註冊表沒有任何自主決策權，只在 LLM 選定 <see cref="GameActionKind"/> 後
/// 回答「怎麼做」。任何綁定缺失、成本不足或尚未實作的動作都必須 fail closed，
/// 絕不可送出半套序列。
/// </summary>
public static class GameActionRegistry
{
    private const int UiSettleMs = 180;
    private static readonly (double X, double Y)[] HousePlacementCandidates =
    [
        (0.58, 0.54), (0.42, 0.54), (0.58, 0.42),
        (0.42, 0.42), (0.50, 0.62), (0.50, 0.38),
    ];

    public static bool TryResolve(
        GameActionKind kind,
        GameHotKeyBindings bindings,
        GameState? state,
        GameAge? currentAge,
        out GameActionProcedure procedure,
        out string blockedReason,
        int housePlacementIndex = 0)
    {
        procedure = null!;
        blockedReason = "";

        switch (kind)
        {
            case GameActionKind.Observe:
            case GameActionKind.Wait:
                procedure = new(kind, [], Postcondition.None, TimeSpan.FromSeconds(30),
                    kind == GameActionKind.Observe ? "重新觀察畫面" : "等待", []);
                return true;

            case GameActionKind.QueueVillager:
                if (state?.Population?.IsUsable != true || state.PopulationCap?.IsUsable != true)
                { blockedReason = "OCR 無法可靠讀取人口，不送出生產村民動作"; return false; }
                var remainingPopulation = state.PopulationCap.Value.GetValueOrDefault() - state.Population.Value.GetValueOrDefault();
                if (remainingPopulation <= 2)
                { blockedReason = $"人口剩餘空間只有 {remainingPopulation}，必須先建造房屋"; return false; }
                return TryTownCenterKeyboard(kind, bindings, state, "queueVillager",
                    [new(TrackedResource.Food, 50)], "生產村民", out procedure, out blockedReason);

            case GameActionKind.AdvanceAge:
                blockedReason = "本階段只驗證村民生產與房屋閉環，升時代尚未開放";
                return false;

            case GameActionKind.BuildHouse:
                return TryBuildHouse(bindings, state, housePlacementIndex, out procedure, out blockedReason);

            case GameActionKind.GatherFood:
            case GameActionKind.GatherWood:
            case GameActionKind.GatherGold:
                blockedReason = $"{kind} 需要世界座標接地，屬 ADR 0015 Phase 2，尚未實作";
                return false;

            default:
                blockedReason = $"未知的動作：{kind}";
                return false;
        }
    }

    private static bool TryBuildHouse(
        GameHotKeyBindings bindings,
        GameState? state,
        int placementIndex,
        out GameActionProcedure procedure,
        out string blockedReason)
    {
        procedure = null!;
        if (!bindings.TryGetKeys("selectIdleVillager", out var idle, out blockedReason)) return false;
        if (!bindings.TryGetKeys("openEconomicBuildings", out var economic, out blockedReason)) return false;
        if (!bindings.TryGetKeys("buildHouse", out var house, out blockedReason)) return false;
        if (!HasAffordableResources(state, [new(TrackedResource.Wood, 25)], out blockedReason)) return false;
        if (state?.Population?.IsUsable != true || state.PopulationCap?.IsUsable != true)
        { blockedReason = "OCR 無法可靠讀取人口，不送出建造房屋動作"; return false; }
        var remaining = state.PopulationCap.Value.GetValueOrDefault() - state.Population.Value.GetValueOrDefault();
        if (remaining > 2)
        { blockedReason = $"人口空間仍有 {remaining}，暫不建造房屋"; return false; }

        var candidate = HousePlacementCandidates[Math.Abs(placementIndex) % HousePlacementCandidates.Length];
        procedure = new(GameActionKind.BuildHouse,
            [
                new(ProcedureStepKind.GameKey, Keys: idle),
                new(ProcedureStepKind.Delay, DelayMs: UiSettleMs),
                new(ProcedureStepKind.GameKey, Keys: economic),
                new(ProcedureStepKind.Delay, DelayMs: UiSettleMs),
                new(ProcedureStepKind.GameKey, Keys: house),
                new(ProcedureStepKind.Delay, DelayMs: UiSettleMs),
                new(ProcedureStepKind.WorldClick, X: candidate.X, Y: candidate.Y),
            ],
            new(PostconditionKind.PopulationCapIncrease), TimeSpan.FromSeconds(90),
            $"建造房屋（候選 {placementIndex % HousePlacementCandidates.Length + 1}）",
            [new(TrackedResource.Wood, 25)]);
        blockedReason = "";
        return true;
    }

    /// <summary>
    /// 城鎮中心命令：先用快捷鍵選取城鎮中心，等面板重繪後再送出該命令的快捷鍵。
    /// 全程零滑鼠、零座標，也不依賴 CommandGridRegion 校準是否正確。
    ///
    /// 中間的 Delay 不可省略：AOE2 的命令鍵是上下文相關的（Q 在村民選中時是 Economic Buildings，
    /// 在城鎮中心選中時才是 Villager），面板尚未切換就送出命令會落到錯誤的上下文。
    /// </summary>
    private static bool TryTownCenterKeyboard(
        GameActionKind kind,
        GameHotKeyBindings bindings,
        GameState? state,
        string commandKeyName,
        IReadOnlyList<ResourceCost> costs,
        string description,
        out GameActionProcedure procedure,
        out string blockedReason)
    {
        procedure = null!;
        if (!bindings.TryGetKeys("selectTownCenter", out var selectKeys, out blockedReason)) return false;
        if (!bindings.TryGetKeys(commandKeyName, out var commandKeys, out blockedReason)) return false;
        if (!HasAffordableResources(state, costs, out blockedReason)) return false;

        procedure = new(kind,
            [
                new(ProcedureStepKind.GameKey, Keys: selectKeys),
                new(ProcedureStepKind.Delay, DelayMs: UiSettleMs),
                new(ProcedureStepKind.GameKey, Keys: commandKeys),
            ],
            new(PostconditionKind.ResourceSpent, costs[0].Resource, costs[0].Amount),
            TimeSpan.FromSeconds(6),
            description,
            costs);
        blockedReason = "";
        return true;
    }

    public static IReadOnlyList<ResourceCost> AdvanceAgeCosts(GameAge current) => current switch
    {
        GameAge.Dark => [new(TrackedResource.Food, 500)],
        GameAge.Feudal => [new(TrackedResource.Food, 800), new(TrackedResource.Gold, 200)],
        GameAge.Castle => [new(TrackedResource.Food, 1000), new(TrackedResource.Gold, 800)],
        _ => [],
    };

    /// <summary>
    /// 成本檢查。OCR 讀不到資源時一律 fail closed——寧可不動作，也不要在資訊不足時送出輸入。
    /// </summary>
    private static bool HasAffordableResources(GameState? state, IReadOnlyList<ResourceCost> costs, out string blockedReason)
    {
        foreach (var cost in costs)
        {
            var observed = Select(state, cost.Resource);
            if (observed?.IsUsable != true)
            { blockedReason = $"OCR 無法可靠讀取{Label(cost.Resource)}，不送出動作"; return false; }
            if (observed.Value.GetValueOrDefault() < cost.Amount)
            { blockedReason = $"{Label(cost.Resource)}不足：需要 {cost.Amount}，目前 {observed.Value.GetValueOrDefault()}"; return false; }
        }
        blockedReason = "";
        return true;
    }

    public static ObservedValue<int>? Select(GameState? state, TrackedResource resource) => resource switch
    {
        TrackedResource.Food => state?.Food,
        TrackedResource.Wood => state?.Wood,
        TrackedResource.Gold => state?.Gold,
        TrackedResource.Stone => state?.Stone,
        _ => null,
    };

    public static string Label(TrackedResource resource) => resource switch
    {
        TrackedResource.Food => "食物",
        TrackedResource.Wood => "木材",
        TrackedResource.Gold => "黃金",
        TrackedResource.Stone => "石料",
        _ => resource.ToString(),
    };
}
