using AgePilot.Core.Automation;
using AgePilot.Core.Observations;

namespace AgePilot.Core.Planning;

public enum MajorPlanIntent { AdvanceAge, StabilizeEconomy, RecoverPlan, Observe }
public enum MediumPlanIntent { GrowEconomy, BalanceResources, PrepareAgeUp, SecurePopulation, RecoverProduction, Observe }
public enum MinorPlanIntent { MaintainVillagerProduction, PreventPopulationBlock, GatherRequiredResource, StartAgeUp, RecoverObservation, WaitForOutcome }
public enum PlanReasonCode { EconomyGrowth, PopulationCap, ResourceRequirement, AgeRequirement, ObservationUnavailable, ObservationContradictory, ActionPending, PrerequisiteMissing, MethodFailed }
public enum PlanScopeEscalation { None, Medium, Major }

/// <summary>Compact, enum-only model response. No field is presentation prose.</summary>
public sealed record CompactGamePlanResponse(
    GameActionKind Action,
    double Confidence,
    int RecheckMs,
    PlanReasonCode Reason,
    PlanScopeEscalation Raise,
    MinorPlanIntent Minor,
    MediumPlanIntent? Medium = null,
    MajorPlanIntent? Major = null);

/// <summary>
/// Deterministic representation mapping only. It never chooses an action or changes an intent.
/// </summary>
public static class CompactGamePlanPresentation
{
    public static DecisionNode Major(MajorPlanIntent intent, GameAge targetAge) => new(
        $"major-{intent}", DecisionLevel.Major,
        intent switch
        {
            MajorPlanIntent.AdvanceAge => $"推進至{AgeLabel(targetAge)}",
            MajorPlanIntent.StabilizeEconomy => "穩定經濟",
            MajorPlanIntent.RecoverPlan => "恢復可執行的發展計畫",
            _ => "維持可靠觀測",
        },
        $"Major intent：{intent}", $"玩家目標：{AgeLabel(targetAge)}",
        intent == MajorPlanIntent.AdvanceAge ? $"進入{AgeLabel(targetAge)}" : "規劃目標完成",
        "目標或可靠觀測失效", DecisionStatus.Active);

    public static DecisionNode Medium(MediumPlanIntent intent) => new(
        $"medium-{intent}", DecisionLevel.Medium,
        intent switch
        {
            MediumPlanIntent.GrowEconomy => "擴大經濟",
            MediumPlanIntent.BalanceResources => "平衡資源",
            MediumPlanIntent.PrepareAgeUp => "準備升時代資源",
            MediumPlanIntent.SecurePopulation => "確保人口空間",
            MediumPlanIntent.RecoverProduction => "恢復生產",
            _ => "等待可靠方法證據",
        },
        $"Medium intent：{intent}", "由本輪 confirmed GameState 與事件支持",
        "方法目標完成", "方法前提或可靠觀測失效", DecisionStatus.Active);

    public static DecisionNode Minor(MinorPlanIntent intent, GameActionKind action, PlanReasonCode reason, GameState state) => new(
        $"minor-{intent}-{action}", DecisionLevel.Minor,
        intent switch
        {
            MinorPlanIntent.MaintainVillagerProduction => "持續生產村民",
            MinorPlanIntent.PreventPopulationBlock => "避免卡人口",
            MinorPlanIntent.GatherRequiredResource => "補足必要資源",
            MinorPlanIntent.StartAgeUp => "開始升時代",
            MinorPlanIntent.RecoverObservation => "恢復可靠觀測",
            _ => "等待目前動作結果",
        },
        Reason(reason), Evidence(reason, state), ExpectedResult(action), FailureCondition(action), DecisionStatus.Active);

    public static string Reason(PlanReasonCode reason) => reason switch
    {
        PlanReasonCode.EconomyGrowth => "維持經濟成長",
        PlanReasonCode.PopulationCap => "人口空間不足",
        PlanReasonCode.ResourceRequirement => "需要補足資源",
        PlanReasonCode.AgeRequirement => "尚未達到目標時代",
        PlanReasonCode.ObservationUnavailable => "關鍵觀測不可用",
        PlanReasonCode.ObservationContradictory => "觀測互相矛盾",
        PlanReasonCode.ActionPending => "等待已送出動作的結果",
        PlanReasonCode.PrerequisiteMissing => "動作前提尚未成立",
        PlanReasonCode.MethodFailed => "目前方法已失效",
        _ => throw new InvalidDataException($"未知 reason code：{reason}"),
    };

    public static string Evidence(PlanReasonCode reason, GameState state) => reason switch
    {
        PlanReasonCode.PopulationCap when state.Population?.IsUsable == true && state.PopulationCap?.IsUsable == true
            => $"confirmed population={state.Population.Value}/{state.PopulationCap.Value}",
        PlanReasonCode.EconomyGrowth when state.Food?.IsUsable == true
            => $"confirmed food={state.Food.Value}",
        PlanReasonCode.AgeRequirement when state.Age is { } age => $"confirmed age={age}",
        PlanReasonCode.ObservationUnavailable => "至少一個必要欄位 unavailable",
        PlanReasonCode.ObservationContradictory => "必要觀測未通過一致性 Gate",
        _ => "由 confirmed GameState 與本輪事件支持",
    };

    public static string ExpectedResult(GameActionKind action) => action switch
    {
        GameActionKind.QueueVillager => "食物扣除代表村民生產指令已接受",
        GameActionKind.BuildHouse => "房屋地基或人口上限變化可被確認",
        GameActionKind.AdvanceAge => "升時代資源扣除或時代欄位變化",
        GameActionKind.Observe => "取得新的可靠觀測",
        GameActionKind.Wait => "等待時間到後重新觀察",
        _ => "註冊表 postcondition 可被確認",
    };

    public static string FailureCondition(GameActionKind action) => action is GameActionKind.Observe or GameActionKind.Wait
        ? "等待後仍無可靠觀測"
        : "註冊表 postcondition 在 timeout 前未成立";

    private static string AgeLabel(GameAge age) => age switch
    {
        GameAge.Feudal => "封建時代",
        GameAge.Castle => "城堡時代",
        GameAge.Imperial => "帝王時代",
        _ => "黑暗時代",
    };
}

public static class CompactGamePlanAdapter
{
    public static PlanningResult Adapt(CompactGamePlanResponse response, SituationContext context,
        PlanUpdateScope scope, DateTimeOffset now)
    {
        if (!Enum.IsDefined(response.Action) || !Enum.IsDefined(response.Reason) ||
            !Enum.IsDefined(response.Raise) || !Enum.IsDefined(response.Minor) ||
            response.Medium is { } mediumValue && !Enum.IsDefined(mediumValue) ||
            response.Major is { } majorValue && !Enum.IsDefined(majorValue))
            return new(null, "Compact GamePlan 含未知 enum");
        if (response.Confidence is < 0 or > 1 || response.RecheckMs is < 250 or > 30000)
            return new(null, "Compact GamePlan confidence 或 recheckMs 無效");
        if (!ValidSemanticCombination(response))
            return new(null, $"Compact GamePlan 語意矛盾：action={response.Action}, minor={response.Minor}, reason={response.Reason}");
        if (scope >= PlanUpdateScope.Medium != response.Medium.HasValue ||
            scope >= PlanUpdateScope.Major != response.Major.HasValue)
            return new(null, "Compact GamePlan scope 欄位不符");
        var raisedScope = response.Raise switch
        {
            PlanScopeEscalation.None => scope,
            PlanScopeEscalation.Medium => PlanUpdateScope.Medium,
            PlanScopeEscalation.Major => PlanUpdateScope.Major,
            _ => scope,
        };
        if (response.Raise != PlanScopeEscalation.None && raisedScope <= scope)
            return new(null, "Compact GamePlan raise 必須提升目前 scope");

        var targetAge = context.Directive?.TargetAge ?? GameAge.Castle;
        var previous = context.PreviousPlan;
        var major = response.Major is { } majorIntent
            ? CompactGamePlanPresentation.Major(majorIntent, targetAge)
            : previous?.MajorDecision;
        var medium = response.Medium is { } mediumIntent
            ? CompactGamePlanPresentation.Medium(mediumIntent)
            : previous?.MediumDecision;
        var minor = CompactGamePlanPresentation.Minor(response.Minor, response.Action, response.Reason, context.State);
        if (major is null || medium is null)
            return new(null, "Compact GamePlan 缺少可沿用的父層");

        var reason = CompactGamePlanPresentation.Reason(response.Reason);
        var decision = new VisualPlayerDecision(
            CompactGamePlanPresentation.Evidence(response.Reason, context.State), minor.Objective, reason,
            new GameAction(response.Action), CompactGamePlanPresentation.ExpectedResult(response.Action),
            response.RecheckMs, response.Confidence);
        var plan = new GamePlan(Guid.NewGuid().ToString("N"), now, now.AddSeconds(60),
            context.Directive?.Strategy ?? "穩定發展經濟並升時代", minor.Objective, reason, response.Confidence,
            VisualDecision: decision, MajorDecision: major, MediumDecision: medium, MinorDecision: minor,
            RequestedUpdateScope: raisedScope);
        return GamePlanValidator.Validate(plan, now);
    }

    private static bool ValidSemanticCombination(CompactGamePlanResponse response) => response.Action switch
    {
        GameActionKind.BuildHouse => response.Minor is MinorPlanIntent.PreventPopulationBlock or
                MinorPlanIntent.MaintainVillagerProduction or MinorPlanIntent.WaitForOutcome &&
                                     response.Reason == PlanReasonCode.PopulationCap,
        GameActionKind.QueueVillager => response.Minor is MinorPlanIntent.MaintainVillagerProduction or
                MinorPlanIntent.WaitForOutcome &&
                                        response.Reason == PlanReasonCode.EconomyGrowth,
        GameActionKind.AdvanceAge => response.Minor is MinorPlanIntent.StartAgeUp or MinorPlanIntent.WaitForOutcome &&
                                     response.Reason == PlanReasonCode.AgeRequirement,
        GameActionKind.GatherFood or GameActionKind.GatherWood or GameActionKind.GatherGold =>
            response.Minor is MinorPlanIntent.GatherRequiredResource or MinorPlanIntent.WaitForOutcome &&
            response.Reason == PlanReasonCode.ResourceRequirement,
        GameActionKind.Observe => response.Minor == MinorPlanIntent.RecoverObservation &&
            response.Reason is PlanReasonCode.ObservationUnavailable or PlanReasonCode.ObservationContradictory or
                PlanReasonCode.PrerequisiteMissing or PlanReasonCode.MethodFailed,
        GameActionKind.Wait => response.Minor is MinorPlanIntent.WaitForOutcome or MinorPlanIntent.RecoverObservation &&
            response.Reason is PlanReasonCode.ActionPending or PlanReasonCode.PrerequisiteMissing or
                PlanReasonCode.ObservationUnavailable,
        _ => false,
    };
}
