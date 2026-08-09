using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using AgePilot.Core;
using AgePilot.Core.Persistence;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Core.Automation;
using AgePilot.Core.Configuration;
using System.Windows.Media;

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
    private readonly AppSettings _settings;
    private readonly AutomationController _automation;
    private Task? _monitorTask;
    private nint _windowHandle;
    private bool _isClickThrough;
    private bool _automationStartHotKeyRegistered;
    private bool _automationStopHotKeyRegistered;
    private string? _currentRecommendationId;

    public OverlayWindow(
        string profilePath,
        AppSettings settings,
        ISessionRepository? sessionRepository = null,
        LocalJsonLineLogger? logger = null)
    {
        InitializeComponent();
        _settings = settings;
        _automation = new AutomationController(settings, logger);
        _coach = new LiveCoachService(profilePath, settings.ScanIntervalMilliseconds, sessionRepository, logger);
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
        UpdateAutomationUi();
        WorldStateText.Text = update.World is null
            ? "世界辨識：等待畫面"
            : $"世界辨識：{update.World.Targets.Count} 個候選 · {update.World.Confidence:P0}";
        StatusText.Text = _isClickThrough ? "滑鼠穿透中（Ctrl+Shift+C 解除）" : update.Status;
        UpdateResources(update.State);

        var advice = update.Recommendations.FirstOrDefault();
        _currentRecommendationId = advice?.Id;
        AdviceTitle.Text = advice?.Title ?? (update.IsConnected ? "目前狀態穩定" : "等待遊戲連線");
        AdviceDescription.Text = advice?.Description ??
            (update.IsConnected ? "沒有需要立即提醒的事項。" : "啟動 AOE2 並進入對局後會自動開始。 ");
        DismissButton.Visibility = advice is null ? Visibility.Collapsed : Visibility.Visible;
        OtherAdviceText.Text = update.Recommendations.Count > 1
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
        var start = ParseGlobalHotKey(_settings.AutomationStartHotKey);
        var stop = ParseGlobalHotKey(_settings.AutomationStopHotKey);
        _automationStopHotKeyRegistered = RegisterHotKey(
            _windowHandle, HotKeyAutomationStop, stop.Modifiers, stop.VirtualKey);
        _automationStartHotKeyRegistered = RegisterHotKey(
            _windowHandle, HotKeyAutomationStart, start.Modifiers, start.VirtualKey);
        if (!_automationStopHotKeyRegistered)
        {
            _automation.Disable("緊急停止熱鍵註冊失敗；自動操作已鎖定");
            AutomationToggleButton.IsEnabled = false;
        }
        else if (!_automationStartHotKeyRegistered)
        {
            _automation.Disable("開啟熱鍵註冊失敗；可使用 Overlay 按鈕，停止熱鍵仍有效");
        }
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
                UpdateAutomationUi();
                handled = true;
                break;
            case HotKeyAutomationStop:
                _automation.Disable("已由緊急停止熱鍵關閉");
                UpdateAutomationUi();
                handled = true;
                break;
        }

        return nint.Zero;
    }

    private void OnToggleClickThrough(object sender, RoutedEventArgs e) => ToggleClickThrough();

    private void OnToggleAutomation(object sender, RoutedEventArgs e)
    {
        if (_automation.IsEnabled)
        {
            var result = System.Windows.MessageBox.Show(this, "是否關閉自動操作？", "AgePilot", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _automation.Disable();
        }
        else
        {
            _automation.Enable();
        }
        UpdateAutomationUi();
    }

    private void UpdateAutomationUi()
    {
        AutomationStateText.Text = _automation.IsEnabled ? "自動：開啟" : "自動：關閉";
        AutomationStateText.Foreground = new SolidColorBrush(_automation.IsEnabled
            ? System.Windows.Media.Color.FromRgb(127, 166, 106)
            : System.Windows.Media.Color.FromRgb(211, 162, 76));
        AutomationToggleButton.Content = _automation.IsEnabled ? "關閉自動" : "開啟自動";
        AutomationDetailText.Text = $"{_automation.LastStatus} · {_settings.AutomationStartHotKey} 開啟 / {_settings.AutomationStopHotKey} 停止";
        MilitaryStateText.Text = _settings.EnableMilitaryAutomation
            ? "軍事：開啟（僅執行已設定序列）"
            : "軍事：關閉";
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
        _automation.Disable("Overlay 已關閉");
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

    private static (uint Modifiers, uint VirtualKey) ParseGlobalHotKey(string gesture)
    {
        var chord = InputSequence.ParseHotKey(gesture);
        uint modifiers = 0;
        foreach (var modifier in chord.Keys.Take(chord.Keys.Count - 1))
        {
            modifiers |= modifier.ToUpperInvariant() switch
            {
                "CTRL" => ModifierControl,
                "SHIFT" => ModifierShift,
                "ALT" => 0x0001,
                _ => throw new InvalidOperationException($"不支援的熱鍵修飾鍵：{modifier}"),
            };
        }
        var key = chord.Keys[^1];
        uint virtualKey = key.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            ? (uint)(0x70 + int.Parse(key[1..]) - 1)
            : key.Length == 1
                ? (uint)char.ToUpperInvariant(key[0])
                : key.ToUpperInvariant() switch
                {
                    "ENTER" => 0x0D,
                    "ESCAPE" => 0x1B,
                    "SPACE" => 0x20,
                    "TAB" => 0x09,
                    "LEFT" => 0x25,
                    "UP" => 0x26,
                    "RIGHT" => 0x27,
                    "DOWN" => 0x28,
                    "HOME" => 0x24,
                    "END" => 0x23,
                    "PAGEUP" => 0x21,
                    "PAGEDOWN" => 0x22,
                    _ => throw new InvalidOperationException($"不支援的全域熱鍵：{key}"),
                };
        return (modifiers, virtualKey);
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
