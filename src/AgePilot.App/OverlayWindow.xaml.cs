using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using AgePilot.Core;
using AgePilot.Core.Persistence;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Core.Configuration;
using System.Windows.Media;
using AgePilot.Core.Automation;
using AgePilot.Core.Planning;

namespace AgePilot.App;

public partial class OverlayWindow : Window
{
    public event EventHandler<LiveCoachUpdate>? CoachUpdated;
    private const int HotKeyToggleVisibility = 0xA901;
    private const int HotKeyToggleClickThrough = 0xA902;
    private const int HotKeyAutomationStart = 0xA903;
    private const int HotKeyAutomationStop = 0xA904;
    private const int WindowMessageHotKey = 0x0312;
    private const int ExtendedStyleIndex = -20;
    private const int ExtendedStyleTransparent = 0x20;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint VirtualKeyA = 0x41;
    private const uint VirtualKeyC = 0x43;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly LiveCoachService _coach;
    private readonly AutomationController _automation;
    private readonly AppSettings _settings;
    private Task? _monitorTask;
    private nint _windowHandle;
    private bool _isClickThrough;
    private string? _currentRecommendationId;

    public OverlayWindow(
        string profilePath,
        AppSettings settings,
        ISessionRepository? sessionRepository = null,
        LocalJsonLineLogger? logger = null)
    {
        InitializeComponent();
        _settings = settings;
        _coach = new LiveCoachService(profilePath, settings, settings.ScanIntervalMilliseconds, sessionRepository, logger);
        _automation = new AutomationController(settings, logger);
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _monitorTask = Task.Run(async () =>
        {
            try
            {
                await _coach.RunAsync(UpdateUiAsync, _cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
        });
    }

    private Task UpdateUiAsync(LiveCoachUpdate update) => Dispatcher.InvokeAsync(() =>
    {
        CoachUpdated?.Invoke(this, update);
        _automation.Handle(update, DateTimeOffset.UtcNow);
        if (_automation.ConsumePlanningEvent() is { } executionEvent) _coach.ReportExecutionEvent(executionEvent);
        UpdatePlanningUi(update.LlmStatus);
        WorldStateText.Text = update.Map?.IsUsable == true
            ? $"小地圖：{update.Map.Archetype} · {update.Map.Confidence:P0}"
            : "小地圖：等待可靠地形";
        StatusText.Text = _isClickThrough ? "滑鼠穿透中（Ctrl+Shift+C 解除）" : update.Status;
        UpdateResources(update.State);

        var advice = update.Recommendations.FirstOrDefault();
        _currentRecommendationId = advice?.Id;
        AdviceTitle.Text = advice?.Title ?? update.Plan?.CurrentGoal ?? (update.IsConnected ? "規劃暫時不可用" : "等待遊戲連線");
        AdviceDescription.Text = advice?.Description ?? update.Plan?.Reason ??
            (update.IsConnected ? "沒有需要立即提醒的事項。" : "啟動 AOE2 並進入對局後會自動開始。 ");
        DismissButton.Visibility = advice is null ? Visibility.Collapsed : Visibility.Visible;
        OtherAdviceText.Text = update.Plan is not null
            ? update.Plan.VisualDecision is { } visual
                ? $"VLM：{visual.Assessment}\n操作：{Describe(visual.Action)} · 信心 {visual.Confidence:P0}\n預期：{visual.ExpectedResult}"
                : $"策略：{update.Plan.Strategy} · 目標：{update.Plan.CurrentGoal}{(update.Plan.ReusedAfterPlanningFailure ? "（沿用上一份有效計畫）" : string.Empty)}"
            : update.Recommendations.Count > 1
                ? "其他：" + string.Join("、", update.Recommendations.Skip(1).Select(item => item.Title))
                : string.Empty;
    }).Task;

    private void UpdateResources(GameState? state)
    {
        WoodText.Text = $"W {Format(state?.Wood?.Value)}";
        FoodText.Text = $"F {Format(state?.Food?.Value)}";
        GoldText.Text = $"G {Format(state?.Gold?.Value)}";
        StoneText.Text = $"S {Format(state?.Stone?.Value)}";
        PopulationText.Text = state?.Population?.Value is { } population && state.PopulationCap?.Value is { } cap
            ? $"P {population}/{cap}"
            : "P …";
    }

    private static string Format(int? value) => value?.ToString() ?? "—";

    private static string Describe(VisualToolAction action) => action.Tool switch
    {
        VisualToolKind.KeySequence => $"按鍵 {string.Join(',', action.Keys)}",
        VisualToolKind.LeftClick => $"左鍵 {action.X:P0},{action.Y:P0}",
        VisualToolKind.RightClick => $"右鍵 {action.X:P0},{action.Y:P0}",
        VisualToolKind.Drag => $"拖曳 {action.X:P0},{action.Y:P0} → {action.EndX:P0},{action.EndY:P0}",
        _ => action.Tool.ToString(),
    };

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDismissAdvice(object sender, RoutedEventArgs e)
    {
        if (_currentRecommendationId is { } id)
        {
            _coach.DismissRecommendation(id);
            _currentRecommendationId = null;
            AdviceTitle.Text = "已略過這項建議";
            AdviceDescription.Text = "條件解除後若再次發生，AgePilot 仍會提醒。";
            DismissButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WindowMessageHook);
        _ = RegisterHotKey(_windowHandle, HotKeyToggleVisibility, ModifierControl | ModifierShift, VirtualKeyA);
        _ = RegisterHotKey(_windowHandle, HotKeyToggleClickThrough, ModifierControl | ModifierShift, VirtualKeyC);
        RegisterAutomationHotKeys();
        AutomationToggleButton.IsEnabled = true;
        UpdateAutomationUi();
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WindowMessageHotKey)
        {
            return nint.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotKeyToggleVisibility:
                Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                handled = true;
                break;
            case HotKeyToggleClickThrough:
                ToggleClickThrough();
                handled = true;
                break;
            case HotKeyAutomationStart:
                _automation.Enable();
                if (_automation.IsEnabled && !_isClickThrough) ToggleClickThrough();
                UpdateAutomationUi();
                handled = true;
                break;
            case HotKeyAutomationStop:
                _automation.Disable("Ctrl+F12 緊急停止");
                if (_isClickThrough) ToggleClickThrough();
                UpdateAutomationUi();
                handled = true;
                break;
        }

        return nint.Zero;
    }

    private void OnToggleClickThrough(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void OnToggleAutomation(object sender, RoutedEventArgs e)
    {
        _automation.Toggle(requireDashboardPermission: false);
        if (_automation.IsEnabled && !_isClickThrough) ToggleClickThrough();
        if (!_automation.IsEnabled && _isClickThrough) ToggleClickThrough();
        UpdateAutomationUi();
    }

    private void UpdateAutomationUi()
    {
        AutomationStateText.Text = _automation.IsEnabled ? "自動發展已啟用" : "LLM 規劃預演";
        AutomationStateText.Foreground = new SolidColorBrush(_automation.IsEnabled
            ? System.Windows.Media.Color.FromRgb(127, 166, 106)
            : System.Windows.Media.Color.FromRgb(211, 162, 76));
        AutomationToggleButton.Content = _automation.IsEnabled ? "停止" : "啟用";
        AutomationDetailText.Text = _automation.LastStatus;
        MilitaryStateText.Text = "發展建築／經濟科技：依 GamePlan；軍隊與戰鬥未啟用";
    }

    private void UpdatePlanningUi(AgePilot.Core.Planning.PlannerRuntimeStatus? status)
    {
        UpdateAutomationUi();
        if (status is null) return;
        if (_automation.IsEnabled)
        {
            AutomationDetailText.Text = $"{_automation.LastStatus} · {status.Message}";
            return;
        }
        AutomationStateText.Text = status.Phase == AgePilot.Core.Planning.PlannerRuntimePhase.Ready
            ? "LLM 已就緒"
            : status.Phase == AgePilot.Core.Planning.PlannerRuntimePhase.Planning
                ? "LLM 規劃中"
                : "LLM 規劃預覽";
        AutomationStateText.Foreground = new SolidColorBrush(status.Phase == AgePilot.Core.Planning.PlannerRuntimePhase.Ready
            ? System.Windows.Media.Color.FromRgb(127, 166, 106)
            : status.Phase == AgePilot.Core.Planning.PlannerRuntimePhase.Error
                ? System.Windows.Media.Color.FromRgb(190, 82, 72)
                : System.Windows.Media.Color.FromRgb(211, 162, 76));
        AutomationDetailText.Text = status.Message;
    }

    private void ToggleClickThrough()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(_windowHandle, ExtendedStyleIndex).ToInt64();
        _isClickThrough = !_isClickThrough;
        var updatedStyle = _isClickThrough
            ? style | ExtendedStyleTransparent
            : style & ~ExtendedStyleTransparent;
        _ = SetWindowLongPtr(_windowHandle, ExtendedStyleIndex, new nint(updatedStyle));
        ClickThroughButton.Content = _isClickThrough ? "●" : "◎";
        StatusText.Text = _isClickThrough ? "滑鼠穿透中（Ctrl+Shift+C 解除）" : "監測中";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_windowHandle != nint.Zero)
        {
            _ = UnregisterHotKey(_windowHandle, HotKeyToggleVisibility);
            _ = UnregisterHotKey(_windowHandle, HotKeyToggleClickThrough);
            _ = UnregisterHotKey(_windowHandle, HotKeyAutomationStart);
            _ = UnregisterHotKey(_windowHandle, HotKeyAutomationStop);
        }

        _cancellation.Cancel();
        _ = (_monitorTask ?? Task.CompletedTask).ContinueWith(
            _ =>
            {
                _coach.Dispose();
                _cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RegisterAutomationHotKeys()
    {
        RegisterConfiguredHotKey(HotKeyAutomationStart, _settings.AutomationStartHotKey);
        RegisterConfiguredHotKey(HotKeyAutomationStop, _settings.AutomationStopHotKey);
    }

    private void RegisterConfiguredHotKey(int id, string gesture)
    {
        var chord = InputSequence.ParseHotKey(gesture);
        uint modifiers = 0;
        foreach (var key in chord.Keys.Take(chord.Keys.Count - 1))
        {
            if (key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= 0x0002;
            if (key.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= 0x0004;
            if (key.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= 0x0001;
        }
        var primary = chord.Keys[^1];
        uint virtualKey = primary.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(primary[1..], out var functionNumber)
            ? (uint)(0x70 + functionNumber - 1)
            : char.ToUpperInvariant(primary[0]);
        _ = RegisterHotKey(_windowHandle, id, modifiers, virtualKey);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newValue);
}
