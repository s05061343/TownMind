using AgePilot.Core;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Vision.Profiles;

namespace AgePilot.App;

internal sealed class AutomationController(AppSettings settings, LocalJsonLineLogger? logger = null)
{
    private readonly WindowsInputSender _input = new();
    private readonly HudProfile _profile = HudProfileLoader.Load(settings.HudProfilePath);
    private readonly HashSet<string> _sentPlans = [];
    private readonly Queue<DateTimeOffset> _recentActions = [];
    private PendingAction? _pending;
    private int _consecutiveFailures;
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;
    private string? _lastFailedPlanId;

    public bool IsEnabled { get; private set; }
    public bool HasPendingAction => _pending is not null;
    public string LastStatus { get; private set; } = "預演模式，不送出輸入";
    public PlanningEvent? LastPlanningEvent { get; private set; }

    public bool Enable(bool requireDashboardPermission = true)
    {
        if (requireDashboardPermission && !settings.EnableAutomationInput)
        { LastStatus = "Dashboard 尚未授權全域啟用"; return false; }
        if (_profile.MinimapRegion is null || _profile.CommandGridRegion is null)
        { LastStatus = "HUD profile 缺少小地圖或命令面板格位校準"; return false; }
        IsEnabled = true; _consecutiveFailures = 0; _pending = null;
        LastStatus = "已啟用，等待 VLM 安全動作";
        logger?.Write("automation.enabled", new { source = "Qwen3-VL" });
        return true;
    }

    public void Disable(string reason = "自動操作已停止")
    {
        IsEnabled = false; _pending = null; LastStatus = reason;
        logger?.Write("automation.disabled", new { reason });
    }

    public void Toggle(bool requireDashboardPermission = true)
    { if (IsEnabled) Disable(); else Enable(requireDashboardPermission); }

    public void Handle(LiveCoachUpdate update, DateTimeOffset now)
    {
        var plan = update.Plan;
        var decision = plan?.VisualDecision;
        if (!IsEnabled) { LastStatus = decision is null ? "預演：等待 VLM 決策" : $"預演：{Describe(decision.Action)}"; return; }
        if (!update.IsConnected || update.Lifecycle == GameLifecycleState.GameEnded)
        { Disable("遊戲已離線或結束，已自動停止"); return; }
        if (update.Lifecycle != GameLifecycleState.GameActive)
        { LastStatus = "已啟用；等待穩定的 Active 遊戲畫面"; return; }
        if (decision is null || plan is null)
        { LastStatus = update.LlmStatus?.Phase == PlannerRuntimePhase.Planning ? "已啟用；VLM 規劃中" : "已啟用；等待 VLM 決策"; return; }

        if (_pending is not null)
        {
            if (now < _pending.NotBefore) { LastStatus = $"等待確認：{_pending.Description}"; return; }
            if (decision.PreviousActionResult == PreviousActionResult.Confirmed)
            { CompletePending("VLM 已確認遊戲結果", now); return; }
            if (decision.PreviousActionResult == PreviousActionResult.Failed || now >= _pending.Deadline)
            { FailOnce(plan.PlanId, decision.PreviousActionResult == PreviousActionResult.Failed ? "VLM 確認動作失敗" : "等待遊戲結果逾時", now); _pending = null; return; }
            if (decision.PreviousActionResult == PreviousActionResult.Uncertain)
            { FailOnce(plan.PlanId, "VLM 無法確認前一動作", now); return; }
            LastStatus = "等待 VLM 確認前一動作"; return;
        }

        if (plan.ExpiresAt <= now || decision.Confidence < 0.8)
        { FailOnce(plan.PlanId, "VLM 決策過期或信心不足", now); return; }
        if (_sentPlans.Contains(plan.PlanId) || now - _lastActionAt < TimeSpan.FromMilliseconds(500)) return;
        while (_recentActions.TryPeek(out var at) && now - at > TimeSpan.FromMinutes(1)) _recentActions.Dequeue();
        if (_recentActions.Count >= 30) { FailOnce(plan.PlanId, "每分鐘原子操作上限已達", now); return; }
        if (!Safe(decision.Action, out var reason)) { FailOnce(plan.PlanId, reason, now); return; }
        if (decision.Action.Tool is VisualToolKind.Observe or VisualToolKind.Wait)
        { _sentPlans.Add(plan.PlanId); LastStatus = decision.Action.Tool == VisualToolKind.Wait ? "VLM 決定等待" : "VLM 決定重新觀察"; return; }

        var sent = _input.TryExecute(decision.Action, _profile.MinimapRegion!.Value, _profile.CommandGridRegion!.Value,
            _profile.CommandGridRows, _profile.CommandGridColumns, out var inputStatus);
        if (!sent) { FailOnce(plan.PlanId, inputStatus, now); return; }

        _sentPlans.Add(plan.PlanId); _recentActions.Enqueue(now); _lastActionAt = now; _consecutiveFailures = 0;
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(decision.RecheckAfterMs, 250, 30000));
        _pending = new(Describe(decision.Action), now + delay, now + delay + TimeSpan.FromSeconds(30));
        LastStatus = $"已執行：{_pending.Description}；等待 VLM 確認";
        LastPlanningEvent = new("visual_action_sent", $"{_pending.Description}；預期：{decision.ExpectedResult}", now);
        logger?.Write("automation.sent", new { plan.PlanId, decision.Action.Tool, decision.Action.Space,
            decision.Action.Target, decision.Action.X, decision.Action.Y, decision.Action.Row, decision.Action.Column,
            inputStatus, decision.ExpectedResult });
    }

    public PlanningEvent? ConsumePlanningEvent()
    { var result = LastPlanningEvent; LastPlanningEvent = null; return result; }

    private void CompletePending(string result, DateTimeOffset now)
    { LastPlanningEvent = new("visual_action_confirmed", result, now); LastStatus = result; _pending = null; _consecutiveFailures = 0; }

    private void FailOnce(string planId, string reason, DateTimeOffset now)
    {
        if (_lastFailedPlanId == planId) return;
        _lastFailedPlanId = planId; _consecutiveFailures++;
        LastStatus = $"已阻擋：{reason}（{_consecutiveFailures}/3）";
        LastPlanningEvent = new("visual_action_blocked", reason, now);
        if (_consecutiveFailures >= 3) Disable($"連續三次無法安全操作：{reason}");
    }

    private static bool Safe(VisualToolAction action, out string reason)
    {
        reason = "";
        if (action.Tool is not (VisualToolKind.Observe or VisualToolKind.Wait) && string.IsNullOrWhiteSpace(action.Target))
        { reason = "滑鼠動作缺少可稽核目標"; return false; }
        if (action.Space == VisualCoordinateSpace.CommandGrid)
        { if (action.Tool != VisualToolKind.LeftClick || action.Row <= 0 || action.Column <= 0) { reason = "命令格位動作無效"; return false; } return true; }
        if (action.Space == VisualCoordinateSpace.Minimap && action.Tool == VisualToolKind.Drag)
        { reason = "小地圖不允許拖曳"; return false; }
        static bool Point(double x, double y) => x is >= 0.01 and <= 0.99 && y is >= 0.065 and <= 0.99;
        if (action.Tool is VisualToolKind.LeftClick or VisualToolKind.RightClick && !Point(action.X, action.Y))
        { reason = "點擊座標位於 HUD 或視窗邊界"; return false; }
        if (action.Tool == VisualToolKind.Drag && (!Point(action.X, action.Y) || !Point(action.EndX, action.EndY) ||
            Math.Sqrt(Math.Pow(action.EndX - action.X, 2) + Math.Pow(action.EndY - action.Y, 2)) > 0.6))
        { reason = "拖曳座標或距離超出安全範圍"; return false; }
        return true;
    }

    private static string Describe(VisualToolAction action) => action.Tool switch
    {
        VisualToolKind.LeftClick when action.Space == VisualCoordinateSpace.CommandGrid => $"左鍵命令格位 {action.Row},{action.Column}（{action.Target}）",
        VisualToolKind.LeftClick => $"左鍵 {action.Space} {action.X:P0},{action.Y:P0}（{action.Target}）",
        VisualToolKind.RightClick => $"右鍵 {action.X:P0},{action.Y:P0}", VisualToolKind.Drag => $"拖曳 {action.X:P0},{action.Y:P0} → {action.EndX:P0},{action.EndY:P0}",
        _ => action.Tool.ToString(),
    };

    private sealed record PendingAction(string Description, DateTimeOffset NotBefore, DateTimeOffset Deadline);
}
