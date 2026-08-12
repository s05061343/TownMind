using AgePilot.Core;
using AgePilot.Core.Automation;
using AgePilot.Core.Configuration;
using AgePilot.Infrastructure.Diagnostics;

namespace AgePilot.App;

internal sealed class AutomationController(AppSettings settings, LocalJsonLineLogger? logger = null)
{
    private readonly WindowsInputSender _input = new();
    private readonly GenericEconomicPlanner _planner = new();
    private DateTimeOffset _lastEconomyAction = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMilitaryAction = DateTimeOffset.MinValue;
    private DateTimeOffset _lastVillagerQueueAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastStrategicAction = DateTimeOffset.MinValue;
    private int? _populationAtLastVillagerQueue;
    private int _militarySequenceIndex;
    private string? _lastWaitingReason;
    private PendingAction? _pendingAction;
    private bool _marketConfirmed;
    private bool _blacksmithConfirmed;

    public bool IsEnabled { get; private set; }
    public string LastStatus { get; private set; } = "自動操作已關閉";

    public void Enable()
    {
        IsEnabled = true;
        LastStatus = "自動操作已開啟；切換至 AOE2 後才會送出按鍵";
        logger?.Write("automation.enabled", new { settings.EnableMilitaryAutomation });
    }

    public void Disable(string reason = "自動操作已關閉")
    {
        IsEnabled = false;
        LastStatus = reason;
        logger?.Write("automation.disabled", new { reason });
    }

    public void Toggle()
    {
        if (IsEnabled) Disable(); else Enable();
    }

    public void Handle(LiveCoachUpdate update, DateTimeOffset now)
    {
        if (!IsEnabled) return;
        if (!update.IsConnected || update.Lifecycle != GameLifecycleState.GameActive)
        {
            LastStatus = update.Lifecycle == GameLifecycleState.GamePaused
                ? "遊戲暫停，自動操作待命"
                : "等待可靠的遊戲狀態";
            return;
        }
        var economy = AutomationPolicy.DecideEconomy(update.State);
        var currentPopulation = update.State?.Population?.Value;
        if (_populationAtLastVillagerQueue is not null && currentPopulation != _populationAtLastVillagerQueue)
        {
            _populationAtLastVillagerQueue = null;
        }
        if (now - _lastEconomyAction >= TimeSpan.FromMilliseconds(settings.EconomyActionIntervalMilliseconds))
        {
            if (economy.Kind == AutomationActionKind.QueueVillager)
            {
                if (_populationAtLastVillagerQueue == currentPopulation &&
                    now - _lastVillagerQueueAt < TimeSpan.FromSeconds(45))
                {
                    SetWaitingStatus("村民已排入，等待人口變化後再操作");
                }
                else
                {
                    try
                    {
                        var sent = _input.TrySend(settings.VillagerProductionSequence, out var status);
                        LastStatus = $"經濟：{status}";
                        logger?.Write("automation.economy", new { sequence = settings.VillagerProductionSequence, sent, status });
                        if (sent)
                        {
                            _populationAtLastVillagerQueue = currentPopulation;
                            _lastVillagerQueueAt = now;
                        }
                    }
                    catch (Exception exception)
                    {
                        LastStatus = $"經濟輸入失敗：{exception.Message}";
                        logger?.Write("automation.failure", new { scope = "economy", type = exception.GetType().Name, exception.Message });
                    }
                    _lastEconomyAction = now;
                }
            }
            else
            {
                SetWaitingStatus(economy.Reason);
            }
        }

        HandleStrategicAction(update, now);

        var military = AutomationPolicy.EnabledMilitarySequences(settings);
        if (!settings.EnableMilitaryAutomation || military.Count == 0 ||
            now - _lastMilitaryAction < TimeSpan.FromMilliseconds(settings.MilitaryActionIntervalMilliseconds)) return;
        if (update.VisionConfidence < 0.65 || update.UnavailableFields > 3)
        {
            LastStatus = "軍事待命：HUD 整體信心不足";
            return;
        }

        var sequence = military[_militarySequenceIndex % military.Count];
        _militarySequenceIndex++;
        try
        {
            var sent = _input.TrySend(sequence, out var militaryStatus);
            LastStatus = $"軍事：{militaryStatus}";
            logger?.Write("automation.military", new { sequence, sent, status = militaryStatus });
        }
        catch (Exception exception)
        {
            LastStatus = $"軍事輸入失敗：{exception.Message}";
            logger?.Write("automation.failure", new { scope = "military", type = exception.GetType().Name, exception.Message });
        }
        _lastMilitaryAction = now;
    }

    private void HandleStrategicAction(LiveCoachUpdate update, DateTimeOffset now)
    {
        if (_pendingAction is not null)
        {
            if (IsConfirmed(_pendingAction, update.State))
            {
                if (_pendingAction.Kind == EconomicActionKind.BuildMarket) _marketConfirmed = true;
                if (_pendingAction.Kind == EconomicActionKind.BuildBlacksmith) _blacksmithConfirmed = true;
                logger?.Write("automation.confirmed", new { action = _pendingAction.Kind.ToString() });
                _pendingAction = null;
            }
            else if (now < _pendingAction.Deadline)
            {
                LastStatus = $"確認中：{_pendingAction.Kind}";
                return;
            }
            else
            {
                logger?.Write("automation.timeout", new { action = _pendingAction.Kind.ToString() });
                _pendingAction = null;
            }
        }

        if (now - _lastStrategicAction < TimeSpan.FromMilliseconds(settings.StrategicActionIntervalMilliseconds)) return;
        var action = _planner.Decide(update.State, update.World, _marketConfirmed, _blacksmithConfirmed);
        if (action.Kind is EconomicActionKind.Wait or EconomicActionKind.QueueVillager)
        {
            if (action.Kind == EconomicActionKind.Wait) SetWaitingStatus(action.Reason);
            return;
        }

        try
        {
            var sent = Execute(action, out var status);
            LastStatus = $"策略：{action.Reason} · {status}";
            logger?.Write("automation.strategy", new { action = action.Kind.ToString(), sent, status });
            _lastStrategicAction = now;
            if (sent && action.Confirmation is not null)
            {
                _pendingAction = new PendingAction(
                    action.Kind,
                    update.State?.Wood?.Value,
                    update.State?.PopulationCap?.Value,
                    now.AddSeconds(action.Kind is EconomicActionKind.AdvanceFeudal or EconomicActionKind.AdvanceCastle ? 150 : 45));
            }
        }
        catch (Exception exception)
        {
            LastStatus = $"策略輸入失敗：{exception.Message}";
            logger?.Write("automation.failure", new { scope = "strategy", action = action.Kind.ToString(), type = exception.GetType().Name, exception.Message });
            _lastStrategicAction = now;
        }
    }

    private bool Execute(EconomicAction action, out string status)
    {
        if ((action.Kind is EconomicActionKind.GatherFood or EconomicActionKind.GatherWood or
            EconomicActionKind.GatherGold or EconomicActionKind.BuildHouse or
            EconomicActionKind.BuildMarket or EconomicActionKind.BuildBlacksmith) &&
            action.Target?.IsActionable != true)
        {
            status = "目標未通過操作證據 Gate，未送出輸入";
            return false;
        }

        return action.Kind switch
        {
            EconomicActionKind.GatherFood or EconomicActionKind.GatherWood or EconomicActionKind.GatherGold =>
                _input.TrySendThenClick(settings.IdleVillagerSelectionSequence, action.Target!.X, action.Target.Y, rightClick: true, out status),
            EconomicActionKind.BuildHouse => _input.TrySendThenClick(
                Join(settings.IdleVillagerSelectionSequence, settings.HouseBuildSequence), action.Target!.X, action.Target.Y, rightClick: false, out status),
            EconomicActionKind.BuildMarket => _input.TrySendThenClick(
                Join(settings.IdleVillagerSelectionSequence, settings.MarketBuildSequence), action.Target!.X, action.Target.Y, rightClick: false, out status),
            EconomicActionKind.BuildBlacksmith => _input.TrySendThenClick(
                Join(settings.IdleVillagerSelectionSequence, settings.BlacksmithBuildSequence), action.Target!.X, action.Target.Y, rightClick: false, out status),
            EconomicActionKind.AdvanceFeudal => _input.TrySend(settings.FeudalUpgradeSequence, out status),
            EconomicActionKind.AdvanceCastle => _input.TrySend(settings.CastleUpgradeSequence, out status),
            _ => ReturnUnsupported(action.Kind, out status),
        };
    }

    private static bool IsConfirmed(PendingAction pending, GameState? state) => pending.Kind switch
    {
        EconomicActionKind.BuildHouse => state?.PopulationCap?.IsUsable == true && state.PopulationCap.Value > pending.PopulationCap,
        EconomicActionKind.BuildMarket or EconomicActionKind.BuildBlacksmith =>
            state?.Wood?.IsUsable == true && pending.Wood is not null && state.Wood.Value <= pending.Wood - 80,
        EconomicActionKind.AdvanceFeudal => state?.Age is GameAge.Feudal or GameAge.Castle or GameAge.Imperial,
        EconomicActionKind.AdvanceCastle => state?.Age is GameAge.Castle or GameAge.Imperial,
        _ => true,
    };

    private static string Join(string first, string second) => $"{first},{second}";

    private static bool ReturnUnsupported(EconomicActionKind kind, out string status)
    {
        status = $"不支援的策略動作：{kind}";
        return false;
    }

    private void SetWaitingStatus(string reason)
    {
        LastStatus = $"經濟待命：{reason}";
        if (reason == _lastWaitingReason) return;
        _lastWaitingReason = reason;
        logger?.Write("automation.waiting", new { reason });
    }

    private sealed record PendingAction(EconomicActionKind Kind, int? Wood, int? PopulationCap, DateTimeOffset Deadline);
}
