using AgePilot.Core;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Diagnostics;

namespace AgePilot.App;

internal sealed class AutomationController(AppSettings settings, LocalJsonLineLogger? logger = null)
{
    private readonly WindowsInputSender _input = new();
    private readonly HashSet<string> _executedPlans = [];
    private int _consecutiveFailures;
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;
    private readonly Queue<DateTimeOffset> _recentActions = [];

    public bool IsEnabled { get; private set; }
    public string LastStatus { get; private set; } = "預演模式，不送出輸入";
    public PlanningEvent? LastPlanningEvent { get; private set; }

    public void Enable(bool requireDashboardPermission = true)
    {
        if (requireDashboardPermission && !settings.EnableAutomationInput)
        { LastStatus = "全域熱鍵尚未授權；可直接點 Overlay 的啟用按鈕"; return; }
        IsEnabled = true; _consecutiveFailures = 0; LastStatus = "已啟用，等待 VLM 原子動作";
        logger?.Write("automation.enabled", new { source = "Qwen3-VL" });
    }

    public void Disable(string reason = "自動操作已停止")
    {
        IsEnabled = false; LastStatus = reason;
        logger?.Write("automation.disabled", new { reason });
    }

    public void Toggle(bool requireDashboardPermission = true)
    { if (IsEnabled) Disable(); else Enable(requireDashboardPermission); }

    public void Handle(LiveCoachUpdate update, DateTimeOffset now)
    {
        var decision = update.Plan?.VisualDecision;
        if (!IsEnabled)
        {
            LastStatus = decision is null ? "預演：等待 VLM 決策" : $"預演：{Describe(decision.Action)}";
            return;
        }
        if (!update.IsConnected || update.Lifecycle != GameLifecycleState.GameActive)
        { Disable("遊戲不在 Active 狀態，已自動停止"); return; }
        if (decision is null) { Fail("VLM 沒有可用決策"); return; }
        if (update.Plan!.ExpiresAt <= now || decision.Confidence < 0.65)
        { Fail("VLM 決策過期或信心不足"); return; }
        if (_executedPlans.Contains(update.Plan.PlanId)) return;
        if (now - _lastActionAt < TimeSpan.FromMilliseconds(500)) return;
        while (_recentActions.TryPeek(out var at) && now - at > TimeSpan.FromMinutes(1)) _recentActions.Dequeue();
        if (_recentActions.Count >= 30) { Fail("每分鐘原子操作上限已達 30 次"); return; }
        if (!Safe(decision.Action, out var unsafeReason)) { Fail(unsafeReason); return; }

        if (decision.Action.Tool is VisualToolKind.Observe or VisualToolKind.Wait)
        {
            _executedPlans.Add(update.Plan.PlanId);
            LastStatus = decision.Action.Tool == VisualToolKind.Wait ? "VLM 決定等待" : "VLM 決定重新觀察";
            LastPlanningEvent = new("visual_observe", $"{decision.Assessment}；{decision.ExpectedResult}", now);
            return;
        }

        var sent = decision.Action.Tool switch
        {
            VisualToolKind.KeySequence => _input.TrySend(string.Join(',', decision.Action.Keys), out _),
            VisualToolKind.LeftClick => _input.TryClick(decision.Action.X, decision.Action.Y, false, out _),
            VisualToolKind.RightClick => _input.TryClick(decision.Action.X, decision.Action.Y, true, out _),
            VisualToolKind.Drag => _input.TryDrag(decision.Action.X, decision.Action.Y,
                decision.Action.EndX, decision.Action.EndY, out _),
            _ => false,
        };
        if (!sent) { Fail("Windows 輸入後端拒絕動作"); return; }

        _executedPlans.Add(update.Plan.PlanId); _lastActionAt = now; _consecutiveFailures = 0;
        _recentActions.Enqueue(now);
        LastStatus = $"已執行：{Describe(decision.Action)}；等待 VLM 看新畫面";
        LastPlanningEvent = new("visual_action_sent",
            $"{Describe(decision.Action)}；預期：{decision.ExpectedResult}", now);
        logger?.Write("automation.sent", new { update.Plan.PlanId, decision.Action.Tool, decision.Action.Keys,
            decision.Action.X, decision.Action.Y, decision.ExpectedResult });
    }

    public PlanningEvent? ConsumePlanningEvent()
    { var result = LastPlanningEvent; LastPlanningEvent = null; return result; }

    private void Fail(string reason)
    {
        _consecutiveFailures++; LastStatus = $"已阻擋：{reason}（{_consecutiveFailures}/3）";
        if (_consecutiveFailures >= 3) Disable($"連續三次無法安全決策：{reason}");
    }

    private static bool Safe(VisualToolAction action, out string reason)
    {
        reason = "";
        if (action.Tool == VisualToolKind.KeySequence)
        {
            try { foreach (var chord in action.Keys) _ = AgePilot.Core.Automation.InputSequence.Parse(chord); }
            catch (Exception ex) { reason = ex.Message; return false; }
            return action.Keys.Count is > 0 and <= 6;
        }
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
        VisualToolKind.KeySequence => $"按鍵 {string.Join(',', action.Keys)}",
        VisualToolKind.LeftClick => $"左鍵 {action.X:P0},{action.Y:P0}",
        VisualToolKind.RightClick => $"右鍵 {action.X:P0},{action.Y:P0}",
        VisualToolKind.Drag => $"拖曳 {action.X:P0},{action.Y:P0} → {action.EndX:P0},{action.EndY:P0}",
        _ => action.Tool.ToString(),
    };
}
