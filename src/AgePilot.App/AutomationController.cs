using AgePilot.Core;
using AgePilot.Core.Configuration;
using AgePilot.Core.Automation;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Vision.Profiles;

namespace AgePilot.App;

/// <summary>
/// 消費 LLM 選定的具名動作，交由 <see cref="GameActionRegistry"/> 的程序執行，
/// 並以 <see cref="ActionOutcomeVerifier"/> 依 OCR 判定結果（ADR 0015）。
///
/// 舊版讓 VLM 自行回報前一動作是否成功，2026-08-13 三場實機 session 共 18 次決策中
/// 沒有任何一次回傳 Confirmed，導致每個動作都累積失敗、三次後自動停用。確認權因此移交本層。
/// </summary>
internal sealed class AutomationController
{
    private readonly AppSettings _settings;
    private readonly MouseCapabilitySession _mouseCapability;
    private readonly LocalJsonLineLogger? _logger;
    private readonly WindowsInputSender _input = new();
    private readonly HudProfile _profile;
    private readonly GameHotKeyBindings _bindings;
    private readonly string? _bindingsError;
    private readonly HashSet<string> _sentPlans = [];
    private readonly Queue<DateTimeOffset> _recentActions = [];
    private PendingAction? _pending;
    private int _consecutiveFailures;
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;
    private string? _lastFailedPlanId;
    private string? _lastLoggedPlanId;
    private int _housePlacementIndex;
    private Task<(bool Sent, string Status)>? _executionTask;
    private ExecutionAttempt? _executionAttempt;
    private CancellationTokenSource _executionCancellation = new();

    public AutomationController(AppSettings settings, MouseCapabilitySession mouseCapability,
        LocalJsonLineLogger? logger = null)
    {
        _settings = settings;
        _mouseCapability = mouseCapability;
        _logger = logger;
        _profile = HudProfileLoader.Load(settings.HudProfilePath);
        try
        {
            _bindings = GameHotKeyBindingsLoader.Load(settings.GameHotKeyProfilePath);
            _bindingsError = null;
        }
        catch (Exception exception)
        {
            // 綁定載入失敗不可讓整個 App 起不來；改為讓所有需要按鍵的動作 fail closed。
            _bindings = GameHotKeyBindings.Empty();
            _bindingsError = exception.Message;
            logger?.Write("automation.bindings_unavailable", new { path = settings.GameHotKeyProfilePath, error = exception.Message });
        }
    }

    public bool IsEnabled { get; private set; }
    public bool HasPendingAction => _pending is not null;
    public string LastStatus { get; private set; } = "預演模式，不送出輸入";
    public PlanningEvent? LastPlanningEvent { get; private set; }

    public bool Enable(bool requireDashboardPermission = true)
    {
        if (_executionTask?.IsCompleted == false)
            return RefuseEnable("前一個輸入程序仍在停止中", "execution_stopping");
        _executionTask = null;
        _executionAttempt = null;
        if (requireDashboardPermission && !_settings.EnableAutomationInput)
            return RefuseEnable("Dashboard 尚未授權全域啟用", "dashboard_permission");
        if (!_mouseCapability.IsValidFor(_input.CurrentGameWindowHandle))
        {
            LastStatus = "正在驗證滑鼠控制能力";
            var probe = _mouseCapability.Run(_input);
            if (!probe.Success) return RefuseEnable(probe.Status, "mouse_probe");
        }
        if (_profile.MinimapRegion is null || _profile.CommandGridRegion is null)
            return RefuseEnable("HUD profile 缺少小地圖或命令面板格位校準", "hud_profile");
        if (!KeyboardReady(out var keyboardReason))
            return RefuseEnable(keyboardReason, "game_keyboard");
        _executionCancellation.Dispose();
        _executionCancellation = new CancellationTokenSource();
        IsEnabled = true; _consecutiveFailures = 0; _pending = null;
        LastStatus = "已啟用，等待 LLM 選擇動作";
        _logger?.Write("automation.enabled", new { source = "Qwen3-VL", bindings = _bindings.Id });
        return true;
    }

    /// <summary>
    /// 啟用被拒絕時必須留下日誌。只更新 LastStatus 的話，使用者看到的是「按了沒反應」，
    /// 而日誌裡一片空白——沒有任何線索可以判斷是哪一道閘門擋下的。
    /// </summary>
    private bool RefuseEnable(string reason, string gate)
    {
        LastStatus = reason;
        _logger?.Write("automation.enable_refused", new
        {
            gate,
            reason,
            _settings.EnableAutomationInput,
            bindings = _bindings.Id,
            bindingsVerified = _bindings.Verified,
            bindingsError = _bindingsError,
        });
        return false;
    }

    public void Disable(string reason = "自動操作已停止")
    {
        _executionCancellation.Cancel();
        IsEnabled = false; _pending = null; LastStatus = reason;
        _logger?.Write("automation.disabled", new { reason });
    }

    public void Toggle(bool requireDashboardPermission = true)
    { if (IsEnabled) Disable(); else Enable(requireDashboardPermission); }

    /// <summary>
    /// 遊戲按鍵的授權檢查。依 ADR 0015，使用者必須明確開啟設定，且快捷鍵設定檔必須已由使用者確認過
    /// （verified=true）。未確認的預設綁定不得用來送出輸入。
    /// </summary>
    private bool KeyboardReady(out string reason)
    {
        if (_bindingsError is not null)
        { reason = $"遊戲快捷鍵設定無法載入：{_bindingsError}"; return false; }
        if (!_bindings.Verified)
        { reason = $"快捷鍵設定 {_bindings.Id} 尚未經使用者確認（verified=false）"; return false; }
        reason = "";
        return true;
    }

    public void Handle(LiveCoachUpdate update, DateTimeOffset now)
    {
        var plan = update.Plan;
        var decision = plan?.VisualDecision;
        if (!IsEnabled) { LastStatus = decision is null ? "預演：等待 LLM 決策" : $"預演：{Describe(decision.Action)}"; return; }
        if (!update.IsConnected || update.Lifecycle == GameLifecycleState.GameEnded)
        { Disable("遊戲已離線或結束，已自動停止"); return; }
        if (update.Lifecycle != GameLifecycleState.GameActive)
        { LastStatus = "已啟用；等待穩定的 Active 遊戲畫面"; return; }

        if (_executionTask is not null) { HandleExecutionCompletion(update, now); return; }

        // pending 的驗證不依賴新計畫：OCR 每 tick 都在更新，動作結果隨時可能確認。
        if (_pending is not null) { HandlePending(update, plan, now); return; }

        if (decision is null || plan is null)
        { LastStatus = update.LlmStatus?.Phase == PlannerRuntimePhase.Planning ? "已啟用；LLM 規劃中" : "已啟用；等待 LLM 決策"; return; }

        if (_lastLoggedPlanId != plan.PlanId)
        {
            _lastLoggedPlanId = plan.PlanId;
            _logger?.Write("automation.decision", new { plan.PlanId, decision.Confidence,
                kind = decision.Action.Kind.ToString(), decision.Action.Quantity, decision.ExpectedResult });
        }

        if (plan.ExpiresAt <= now || decision.Confidence < 0.8)
        { FailOnce(plan.PlanId, "LLM 決策過期或信心不足", now, plan.MinorDecision?.NodeId); return; }
        if (_sentPlans.Contains(plan.PlanId) || now - _lastActionAt < TimeSpan.FromMilliseconds(500)) return;
        while (_recentActions.TryPeek(out var at) && now - at > TimeSpan.FromMinutes(1)) _recentActions.Dequeue();
        if (_recentActions.Count >= 30)
        { FailOnce(plan.PlanId, "每分鐘原子操作上限已達", now, plan.MinorDecision?.NodeId); return; }

        if (decision.Action.Kind is GameActionKind.Observe or GameActionKind.Wait)
        {
            _sentPlans.Add(plan.PlanId);
            LastStatus = decision.Action.Kind == GameActionKind.Wait
                ? $"LLM 決定等待：{decision.Reason}" : $"LLM 決定重新觀察：{decision.Reason}";
            _logger?.Write("automation.waiting", new { plan.PlanId, kind = decision.Action.Kind.ToString(),
                decision.Reason, decision.Assessment });
            var waitDelay = TimeSpan.FromMilliseconds(Math.Clamp(decision.RecheckAfterMs, 250, 30000));
            _pending = new(Describe(decision.Action), plan.PlanId, null, null, now + waitDelay, now + waitDelay);
            return;
        }

        if (!GameActionRegistry.TryResolve(decision.Action.Kind, _bindings, update.State, update.State?.Age,
                out var procedure, out var blockedReason, _housePlacementIndex))
        { FailOnce(plan.PlanId, blockedReason, now, plan.MinorDecision?.NodeId); return; }

        if (procedure.Steps.Any(step => step.Kind == ProcedureStepKind.GameKey) && !KeyboardReady(out var keyboardReason))
        { FailOnce(plan.PlanId, keyboardReason, now, plan.MinorDecision?.NodeId); return; }

        var actionId = Guid.NewGuid().ToString("N");
        _executionAttempt = new(procedure, plan.PlanId, actionId, update.State ?? new GameState(),
            decision.ExpectedResult, plan.MinorDecision?.NodeId, now);
        _executionTask = Task.Run(() =>
        {
            var sent = _input.TryExecuteProcedure(procedure, _profile.MinimapRegion!.Value, _profile.CommandGridRegion!.Value,
                _profile.CommandGridRows, _profile.CommandGridColumns, out var status, _executionCancellation.Token);
            return (sent, status);
        });
        LastStatus = $"正在執行：{procedure.Description}";
    }

    private void HandleExecutionCompletion(LiveCoachUpdate update, DateTimeOffset now)
    {
        if (_executionTask?.IsCompleted != true) { LastStatus = $"正在執行：{_executionAttempt?.Procedure.Description}"; return; }
        var task = _executionTask;
        var attempt = _executionAttempt!;
        _executionTask = null;
        _executionAttempt = null;
        (bool sent, string inputStatus) result;
        try { result = task.GetAwaiter().GetResult(); }
        catch (Exception exception) { result = (false, $"輸入程序例外：{exception.Message}"); }
        if (!result.sent) { FailOnce(attempt.PlanId, result.inputStatus, now, attempt.NodeId); return; }

        _sentPlans.Add(attempt.PlanId); _recentActions.Enqueue(now); _lastActionAt = now; _consecutiveFailures = 0;
        var baseline = new ActionOutcomeBaseline(attempt.Procedure.Kind, attempt.Procedure.Post, attempt.Baseline,
            attempt.StartedAt, attempt.StartedAt + attempt.Procedure.Timeout);
        _pending = new(attempt.Procedure.Description, attempt.PlanId, attempt.ActionId, baseline,
            attempt.StartedAt, attempt.StartedAt + attempt.Procedure.Timeout);
        LastStatus = $"已執行：{attempt.Procedure.Description}；等待 HUD 數值確認";
        LastPlanningEvent = new("visual_action_sent", $"{attempt.Procedure.Description}；預期：{attempt.ExpectedResult}", now,
            PlanUpdateScope.Minor, attempt.NodeId);
        _logger?.Write("automation.sent", new { attempt.PlanId, kind = attempt.Procedure.Kind.ToString(),
            attempt.Procedure.Description, inputStatus = result.inputStatus, postcondition = attempt.Procedure.Post.Kind.ToString(),
            resource = attempt.Procedure.Post.Resource?.ToString(), attempt.Procedure.Post.Amount, attempt.ExpectedResult });
    }

    private void HandlePending(LiveCoachUpdate update, GamePlan? plan, DateTimeOffset now)
    {
        var pending = _pending!;
        var nodeId = plan?.MinorDecision?.NodeId;

        // Observe／Wait 沒有遊戲側後果，等待時間到就重新觀察。
        if (pending.Baseline is null)
        {
            if (now < pending.NotBefore) { LastStatus = $"等待中：{pending.Description}"; return; }
            LastPlanningEvent = new("visual_wait_elapsed", $"重新觀察：{pending.Description}", now, PlanUpdateScope.Minor, nodeId);
            LastStatus = $"等待完成，重新觀察：{pending.Description}";
            _pending = null;
            return;
        }

        if (pending.Baseline.Kind == GameActionKind.BuildHouse && !pending.FoundationConfirmed)
        {
            var beforeWood = pending.Baseline.State.Wood;
            var afterWood = update.State?.Wood;
            if (beforeWood?.IsUsable == true && afterWood?.IsUsable == true &&
                beforeWood.Value.GetValueOrDefault() - afterWood.Value.GetValueOrDefault() >= 13)
            {
                pending = pending with { FoundationConfirmed = true };
                _pending = pending;
                LastStatus = "房屋地基已確認；等待人口上限增加";
            }
            else if (now - pending.Baseline.StartedAt >= TimeSpan.FromSeconds(2))
            {
                _housePlacementIndex++;
                FailOnce(pending.PlanId, "房屋落點後兩秒內未觀察到木材扣除", now, nodeId);
                _pending = null;
                return;
            }
            else
            {
                LastStatus = "等待確認房屋地基";
                return;
            }
        }

        var result = ActionOutcomeVerifier.Evaluate(pending.Baseline, update.State, history: null, now, out var status);
        switch (result)
        {
            case PreviousActionResult.Confirmed:
                _logger?.Write("automation.confirmed", new { planId = pending.PlanId, pending.Description, status });
                LastPlanningEvent = new("visual_action_confirmed", status, now, PlanUpdateScope.Minor, nodeId,
                    PreviousActionResult.Confirmed);
                LastStatus = $"已確認：{pending.Description}（{status}）";
                _pending = null;
                _consecutiveFailures = 0;
                return;

            case PreviousActionResult.Failed:
                if (pending.Baseline?.Kind == GameActionKind.BuildHouse) _housePlacementIndex++;
                FailOnce(pending.PlanId, $"{pending.Description}：{status}", now, nodeId);
                _pending = null;
                return;

            default:
                LastStatus = $"等待確認：{pending.Description}（{status}）";
                return;
        }
    }

    public PlanningEvent? ConsumePlanningEvent()
    { var result = LastPlanningEvent; LastPlanningEvent = null; return result; }

    private void FailOnce(string planId, string reason, DateTimeOffset now, string? nodeId)
    {
        if (_lastFailedPlanId == planId) return;
        _lastFailedPlanId = planId; _consecutiveFailures++;
        LastStatus = $"已阻擋：{reason}（{_consecutiveFailures}/3）";
        LastPlanningEvent = new("visual_action_blocked", reason, now, PlanUpdateScope.Minor, nodeId, PreviousActionResult.Failed);
        _logger?.Write("automation.blocked", new { planId, reason, consecutiveFailures = _consecutiveFailures });
        if (_consecutiveFailures >= 3) Disable($"連續三次無法安全操作：{reason}");
    }

    public static string Describe(GameAction action) => action.Kind switch
    {
        GameActionKind.Observe => "重新觀察畫面",
        GameActionKind.Wait => "等待",
        GameActionKind.QueueVillager => action.Quantity > 1 ? $"生產村民 ×{action.Quantity}" : "生產村民",
        GameActionKind.AdvanceAge => "升時代",
        GameActionKind.GatherFood => "指派村民採集食物",
        GameActionKind.GatherWood => "指派村民採集木材",
        GameActionKind.GatherGold => "指派村民採集黃金",
        GameActionKind.BuildHouse => "建造房屋",
        _ => action.Kind.ToString(),
    };

    private sealed record PendingAction(
        string Description,
        string PlanId,
        string? ActionId,
        ActionOutcomeBaseline? Baseline,
        DateTimeOffset NotBefore,
        DateTimeOffset Deadline,
        bool FoundationConfirmed = false);

    private sealed record ExecutionAttempt(GameActionProcedure Procedure, string PlanId, string ActionId,
        GameState Baseline, string ExpectedResult, string? NodeId, DateTimeOffset StartedAt);

    private static object? Snapshot(GameState? state) => state is null ? null : new
    {
        age = state.Age?.ToString(),
        food = state.Food?.IsUsable == true ? state.Food.Value : null,
        wood = state.Wood?.IsUsable == true ? state.Wood.Value : null,
        gold = state.Gold?.IsUsable == true ? state.Gold.Value : null,
        stone = state.Stone?.IsUsable == true ? state.Stone.Value : null,
        population = state.Population?.IsUsable == true ? state.Population.Value : null,
        populationCap = state.PopulationCap?.IsUsable == true ? state.PopulationCap.Value : null,
    };
}
