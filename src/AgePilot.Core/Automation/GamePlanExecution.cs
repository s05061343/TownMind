using AgePilot.Core.Planning;

namespace AgePilot.Core.Automation;

public enum ExecutableActionKind { KeyboardSequence, TargetedLeftClick, TargetedRightClick, ObserveOnly }

public sealed record ExecutableAction(
    string ActionId,
    string PlanId,
    PlannedActionKind SourceIntent,
    ExecutableActionKind Kind,
    string InputSequence,
    WorldTargetKind? TargetKind,
    TimeSpan Timeout,
    string SuccessCondition,
    string Description);

public sealed record ActionTranslationResult(ExecutableAction? Action, string? BlockedReason)
{
    public bool Success => Action is not null && BlockedReason is null;
    public static ActionTranslationResult Blocked(string reason) => new(null, reason);
}

public sealed record DevelopmentActionBindings(
    string VillagerSequence,
    string IdleVillagerSequence,
    IReadOnlyDictionary<string, string> BuildingSequences,
    IReadOnlyDictionary<string, string> TechnologySequences,
    IReadOnlyDictionary<string, string> AgeSequences);

public static class GamePlanActionTranslator
{
    public static ActionTranslationResult Translate(GamePlan? plan, DevelopmentActionBindings bindings)
    {
        if (plan is null) return ActionTranslationResult.Blocked("沒有可執行的 GamePlan");
        var action = plan.Actions.OrderByDescending(item => item.Priority).FirstOrDefault();
        if (action is null) return ActionTranslationResult.Blocked("GamePlan 沒有動作");
        var id = $"{plan.PlanId}:{action.Intent}:{action.TargetId}:{action.Priority}";

        return action.Intent switch
        {
            PlannedActionKind.QueueVillager => Keyboard(id, plan, action, bindings.VillagerSequence, "生產村民"),
            PlannedActionKind.QueueUnit when action.TargetId.Equals("villager", StringComparison.OrdinalIgnoreCase) =>
                Keyboard(id, plan, action, bindings.VillagerSequence, "生產村民"),
            PlannedActionKind.AdvanceFeudal => FromMap(id, plan, action, "feudal-age", bindings.AgeSequences, "升封建時代"),
            PlannedActionKind.AdvanceCastle => FromMap(id, plan, action, "castle-age", bindings.AgeSequences, "升城堡時代"),
            PlannedActionKind.AdvanceAge => FromMap(id, plan, action, action.TargetId, bindings.AgeSequences, "升時代"),
            PlannedActionKind.ResearchTechnology => FromMap(id, plan, action, action.TargetId, bindings.TechnologySequences, "研發科技"),
            PlannedActionKind.BuildHouse => Targeted(id, plan, action, "house", bindings, WorldTargetKind.OpenBuildArea, false),
            PlannedActionKind.BuildBuilding => Targeted(id, plan, action, action.TargetId, bindings, WorldTargetKind.OpenBuildArea, false),
            PlannedActionKind.GatherFood => Gather(id, plan, action, bindings, WorldTargetKind.Food),
            PlannedActionKind.GatherWood => Gather(id, plan, action, bindings, WorldTargetKind.Wood),
            PlannedActionKind.GatherGold => Gather(id, plan, action, bindings, WorldTargetKind.Gold),
            PlannedActionKind.AssignWorkers => ActionTranslationResult.Blocked("AssignWorkers 必須先由可靠的工作人口觀測解析成單一採集動作"),
            PlannedActionKind.Wait or PlannedActionKind.Reobserve => new(new(id, plan.PlanId, action.Intent,
                ExecutableActionKind.ObserveOnly, "", null, TimeSpan.FromSeconds(action.RecheckSeconds),
                action.SuccessCondition, action.Reason), null),
            _ => ActionTranslationResult.Blocked($"首版尚不執行 {action.Intent}")
        };
    }

    private static ActionTranslationResult Keyboard(string id, GamePlan plan, PlannedAction action, string sequence, string label) =>
        string.IsNullOrWhiteSpace(sequence) ? ActionTranslationResult.Blocked($"{label}沒有已設定輸入序列") :
        new(new(id, plan.PlanId, action.Intent, ExecutableActionKind.KeyboardSequence, sequence, null,
            TimeSpan.FromSeconds(action.RecheckSeconds), action.SuccessCondition, $"{label} ×{Math.Max(1, action.Quantity)}"), null);

    private static ActionTranslationResult FromMap(string id, GamePlan plan, PlannedAction action, string key,
        IReadOnlyDictionary<string, string> map, string label) =>
        string.IsNullOrWhiteSpace(key) || !map.TryGetValue(key, out var sequence)
            ? ActionTranslationResult.Blocked($"{label} {key} 沒有合法且已設定的輸入序列")
            : Keyboard(id, plan, action, sequence, $"{label} {key}");

    private static ActionTranslationResult Targeted(string id, GamePlan plan, PlannedAction action, string key,
        DevelopmentActionBindings bindings, WorldTargetKind target, bool rightClick) =>
        string.IsNullOrWhiteSpace(key) || !bindings.BuildingSequences.TryGetValue(key, out var sequence)
            ? ActionTranslationResult.Blocked($"建築 {key} 沒有合法且已設定的輸入序列")
            : new(new(id, plan.PlanId, action.Intent,
                rightClick ? ExecutableActionKind.TargetedRightClick : ExecutableActionKind.TargetedLeftClick,
                $"{bindings.IdleVillagerSequence},{sequence}", target, TimeSpan.FromSeconds(action.RecheckSeconds),
                action.SuccessCondition, $"建造 {key} ×{Math.Max(1, action.Quantity)}"), null);

    private static ActionTranslationResult Gather(string id, GamePlan plan, PlannedAction action,
        DevelopmentActionBindings bindings, WorldTargetKind target) =>
        new(new(id, plan.PlanId, action.Intent, ExecutableActionKind.TargetedRightClick,
            bindings.IdleVillagerSequence, target, TimeSpan.FromSeconds(action.RecheckSeconds),
            action.SuccessCondition, $"指派 1 名村民採集 {target}"), null);
}

public enum AutomationMode { Preview, Armed }

public sealed class PlanExecutionCoordinator
{
    private readonly HashSet<string> _completedOrSent = [];
    public AutomationMode Mode { get; private set; } = AutomationMode.Preview;
    public ActionExecutionState? Current { get; private set; }
    public string Status { get; private set; } = "預演模式，不送出輸入";

    public void Arm() { Mode = AutomationMode.Armed; Status = "已啟用，等待安全動作"; }
    public void Stop(string reason = "使用者緊急停止") { Mode = AutomationMode.Preview; Current = null; Status = reason; }
    public void Block(string reason) { Status = $"已阻擋：{reason}"; }

    public ActionExecutionState Prepare(ExecutableAction action, DateTimeOffset now, IReadOnlyList<ActionPrecondition> gates)
    {
        if (Mode != AutomationMode.Armed) gates = gates.Append(new("armed", false, "目前是預演模式")).ToArray();
        if (_completedOrSent.Contains(action.ActionId)) gates = gates.Append(new("idempotency", false, "同一計畫動作已送出")).ToArray();
        Current = ActionExecutionState.Start(action.ActionId, now, action.Timeout, gates);
        Status = Current.Status;
        return Current;
    }

    public void MarkSent()
    {
        if (Current?.Phase != ActionExecutionPhase.Ready) return;
        _completedOrSent.Add(Current.ActionId);
        Current = Current.MarkSent();
        Status = Current.Status;
    }

    public ActionExecutionState? Observe(bool confirmed, DateTimeOffset now)
    {
        if (Current is null) return null;
        Current = Current.Observe(confirmed, now);
        Status = Current.Status;
        return Current;
    }
}
