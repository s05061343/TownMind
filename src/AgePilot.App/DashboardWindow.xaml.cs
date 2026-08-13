using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AgePilot.Core.Configuration;
using AgePilot.Vision.Capture;
using AgePilot.Infrastructure.Persistence;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using AgePilot.Infrastructure.Diagnostics;
using AgePilot.Infrastructure.Planning;
using AgePilot.Core.Planning;
using AgePilot.Core;
using AgePilot.Core.Automation;
using AgePilot.Core.History;
using AgePilot.Vision.Images;
using AgePilot.Vision.Geometry;
using System.ComponentModel;
using System.Diagnostics;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AgePilot.App;

public partial class DashboardWindow : Window
{
    private readonly JsonSettingsStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SqliteSessionRepository _sessions = SqliteSessionRepository.CreateDefault();
    private readonly MouseCapabilitySession _mouseCapability = new();
    private AppSettings _settings;
    private OverlayWindow? _overlay;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _trayOverlayItem;
    private readonly Drawing.Icon? _applicationIcon;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _allowExit;
    private LlamaServerPlanner? _sharedPlanner;
    private bool _llmHealthCheckInProgress;

    public DashboardWindow(JsonSettingsStore store)
    {
        InitializeComponent();
        _store = store;
        _trayOverlayItem = new Forms.ToolStripMenuItem("啟動 Overlay", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add(new Forms.ToolStripMenuItem("開啟 Dashboard", null, (_, _) => Dispatcher.Invoke(ShowDashboard)));
        trayMenu.Items.Add(_trayOverlayItem);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(new Forms.ToolStripMenuItem("結束 AgePilot", null, (_, _) => Dispatcher.Invoke(ExitApplication)));
        _applicationIcon = Environment.ProcessPath is null
            ? null
            : Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon ?? Drawing.SystemIcons.Application,
            Text = "AgePilot",
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
        _settings = LoadSafely();
        ApplySettings();
        _sharedPlanner = new LlamaServerPlanner(_settings,
            _settings.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null);
        _timer.Tick += async (_, _) => { RefreshGame(); await RefreshLlmHealthAsync(); };
        Loaded += async (_, _) =>
        {
            RefreshGame(); _timer.Start();
            await Task.WhenAll(RefreshHistoryAsync(), StartLlmAtStartupAsync());
        };
        Closing += OnWindowClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideToTray(); };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _lifetimeCancellation.Cancel();
            _overlay?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon?.Dispose();
            _sharedPlanner?.Dispose();
            _lifetimeCancellation.Dispose();
        };
    }

    private AppSettings LoadSafely()
    {
        try { return _store.Load(); }
        catch (Exception exception) { MessageText.Text = $"設定讀取失敗，使用預設值：{exception.Message}"; return new AppSettings(); }
    }

    private void ApplySettings()
    {
        ProfilePathText.Text = _settings.HudProfilePath;
        OpacitySlider.Value = _settings.OverlayOpacity;
        SessionRecordingCheck.IsChecked = _settings.EnableSessionRecording;
        LocalDiagnosticsCheck.IsChecked = _settings.EnableLocalDiagnostics;
        AutomationInputCheck.IsChecked = _settings.EnableAutomationInput;
        AutomationStartHotKeyText.Text = _settings.AutomationStartHotKey;
        AutomationStopHotKeyText.Text = _settings.AutomationStopHotKey;
        LlamaRuntimePathText.Text = _settings.LlamaRuntimePath;
        LlmModelPathText.Text = _settings.LlmModelPath;
        VisionProjectorPathText.Text = _settings.VisionProjectorPath;
        TargetAgeCombo.SelectedItem = TargetAgeCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _settings.TargetAge.ToString(), StringComparison.Ordinal));
        if (TargetAgeCombo.SelectedIndex < 0) TargetAgeCombo.SelectedIndex = 1;
        SetLlmStatus(PlannerRuntimeStatus.NotConfigured("尚未測試；啟動 Overlay 後會自動載入"));
        ScanIntervalCombo.SelectedItem = ScanIntervalCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Content?.ToString() == _settings.ScanIntervalMilliseconds.ToString());
        if (ScanIntervalCombo.SelectedIndex < 0) ScanIntervalCombo.SelectedIndex = 1;
    }

    private void RefreshGame()
    {
        var game = new WindowsGameWindowLocator().Find();
        GameStatusText.Text = game is null ? "等待 AOE2 DE" : $"已連線：{game.Title}";
        GameStatusDot.Fill = new SolidColorBrush(game is null ? System.Windows.Media.Color.FromRgb(211, 162, 76) : System.Windows.Media.Color.FromRgb(127, 166, 106));
        if (_mouseCapability.IsVerified && !_mouseCapability.IsValidFor(game?.Handle ?? nint.Zero))
        {
            MouseStatusText.Text = "滑鼠：AOE2 視窗已更換，請重新測試";
            MouseStatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 162, 76));
        }
    }

    private void OnTestMouse(object sender, RoutedEventArgs e)
    {
        TestMouseButton.IsEnabled = false;
        try
        {
            var result = _mouseCapability.Run(new WindowsInputSender());
            MouseStatusText.Text = $"滑鼠：{result.Status}";
            MouseStatusDot.Fill = new SolidColorBrush(result.Success
                ? System.Windows.Media.Color.FromRgb(127, 166, 106)
                : System.Windows.Media.Color.FromRgb(190, 82, 72));
            MessageText.Text = result.Status;
            if (result.Success) Activate();
        }
        finally { TestMouseButton.IsEnabled = true; }
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = ReadSettings();
            _store.Save(_settings);
            MessageText.Text = $"設定已儲存至 {_store.Path}；LLM runtime／模型變更請按「重新啟動 LLM」套用";
        }
        catch (Exception exception) { MessageText.Text = $"無法儲存設定：{exception.Message}"; }
    }

    private void OnBrowseLlamaRuntime(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "選擇包含 hip 與 vulkan 子目錄的 llama.cpp runtime 目錄",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(LlamaRuntimePathText.Text) ? LlamaRuntimePathText.Text : string.Empty,
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) LlamaRuntimePathText.Text = dialog.SelectedPath;
    }

    private void OnBrowseLlmModel(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇 GGUF 模型",
            Filter = "GGUF 模型 (*.gguf)|*.gguf|所有檔案 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) LlmModelPathText.Text = dialog.FileName;
    }

    private void OnBrowseVisionProjector(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇 Qwen3-VL mmproj GGUF",
            Filter = "GGUF 視覺編碼器 (*.gguf)|*.gguf|所有檔案 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true) VisionProjectorPathText.Text = dialog.FileName;
    }

    private async void OnTestLlm(object sender, RoutedEventArgs e)
    {
        TestLlmButton.IsEnabled = false;
        try
        {
            var candidate = ReadSettings();
            _settings = candidate;
            _store.Save(candidate);
            _sharedPlanner ??= new LlamaServerPlanner(candidate,
                candidate.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null);
            var planner = _sharedPlanner;
            SetLlmStatus(new(PlannerRuntimePhase.Starting, "正在確認常駐 llama-server 狀態…"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(190));
            var ready = await planner.CheckReadyAsync(timeout.Token);
            if (ready.Phase != PlannerRuntimePhase.Ready)
            {
                SetLlmStatus(ready);
                LlmResultText.Text = $"LLM backend 未就緒：{ready.Message}";
                return;
            }
            var window = new WindowsGameWindowLocator().Find();
            if (window is null)
            {
                SetLlmStatus(new(PlannerRuntimePhase.Ready, $"{ready.Message}；啟動遊戲後才能測試圖片理解", ready.Backend));
                LlmResultText.Text = "找不到 AOE2 視窗，未進行畫面推論。";
                return;
            }
            var frame = await new WindowsGdiFrameCapture().CaptureAsync(window, timeout.Token);
            var images = VisualPromptImageEncoder.Encode(frame.BgraPixels.Span, frame.Width, frame.Height,
                new NormalizedRect(0, 0.66, 0.47, 0.34), new NormalizedRect(0.80, 0.67, 0.20, 0.33));
            var now = DateTimeOffset.UtcNow;
            var inference = Stopwatch.StartNew();
            var result = await planner.PlanAsync(new SituationContext(new GameState(),
                GameHistorySummarizer.Summarize(new GameHistory(), TimeSpan.FromSeconds(1), now), null, null, [], now,
                new VisualObservation(frame.Width, frame.Height, images, "AOE2 screenshot vision readiness test", null, null)), timeout.Token);
            if (result.Success && result.Plan?.VisualDecision is { } decision)
            {
                var plan = result.Plan;
                var runtime = planner.RuntimeStatus;
                LlmResultText.Text = $"Plan：{plan.PlanId}\nBackend：{runtime.Backend ?? "未知"}\n耗時：{inference.ElapsedMilliseconds} ms\n" +
                    $"大判斷：{plan.MajorDecision?.Objective}\n中判斷：{plan.MediumDecision?.Objective}\n小判斷：{plan.MinorDecision?.Objective}\n" +
                    $"模型看到：{decision.Assessment}\n決定：{AutomationController.Describe(decision.Action)}\n理由：{decision.Reason}\n" +
                    $"信心：{decision.Confidence:P1}\n\n原始 JSON：\n{planner.LastRawResponse}";
                SetLlmStatus(new(PlannerRuntimePhase.Ready, $"實測完成：{AutomationController.Describe(decision.Action)}，信心 {decision.Confidence:P0}", runtime.Backend));
            }
            else
            {
                SetLlmStatus(new(PlannerRuntimePhase.Error, $"LLM 實測失敗：{result.Error}", ready.Backend));
                LlmResultText.Text = $"實測失敗：{result.Error}\n\n原始回覆：\n{planner.LastRawResponse}";
            }
        }
        catch (OperationCanceledException) { SetLlmStatus(new(PlannerRuntimePhase.Error, "LLM 實測逾時")); LlmResultText.Text = "LLM 實測逾時。"; }
        catch (Exception exception) { SetLlmStatus(new(PlannerRuntimePhase.Error, exception.Message)); LlmResultText.Text = exception.ToString(); }
        finally { TestLlmButton.IsEnabled = true; }
    }

    private async void OnRestartLlm(object sender, RoutedEventArgs e)
    {
        RestartLlmButton.IsEnabled = false;
        TestLlmButton.IsEnabled = false;
        try
        {
            var candidate = ReadSettings();
            _settings = candidate;
            _store.Save(candidate);
            _sharedPlanner ??= new LlamaServerPlanner(candidate,
                candidate.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null);
            SetLlmStatus(new(PlannerRuntimePhase.Starting, "正在重新啟動 llama-server 並載入模型…"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(190));
            var status = await _sharedPlanner.RestartAsync(candidate, timeout.Token);
            SetLlmStatus(status);
            LlmResultText.Text = status.Phase == PlannerRuntimePhase.Ready
                ? $"llama-server 已由使用者重新啟動：{status.Backend}"
                : $"llama-server 重新啟動失敗：{status.Message}";
        }
        catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested)
        {
            SetLlmStatus(new(PlannerRuntimePhase.Error, "重新啟動 LLM 逾時"));
        }
        catch (Exception exception)
        {
            SetLlmStatus(new(PlannerRuntimePhase.Error, exception.Message));
        }
        finally
        {
            RestartLlmButton.IsEnabled = true;
            TestLlmButton.IsEnabled = true;
        }
    }

    private async Task StartLlmAtStartupAsync()
    {
        if (_sharedPlanner is null) return;
        SetLlmStatus(new(PlannerRuntimePhase.Starting, "AgePilot 已啟動；正在載入 llama-server…"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(190));
            SetLlmStatus(await _sharedPlanner.CheckReadyAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested)
        {
            SetLlmStatus(new(PlannerRuntimePhase.Error, "AgePilot 啟動時載入 LLM 逾時；請按「重新啟動 LLM」"));
        }
    }

    private async Task RefreshLlmHealthAsync()
    {
        if (_sharedPlanner is null || _llmHealthCheckInProgress ||
            _sharedPlanner.RuntimeStatus.Phase != PlannerRuntimePhase.Ready) return;
        _llmHealthCheckInProgress = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            SetLlmStatus(await _sharedPlanner.CheckReadyAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested) { }
        finally { _llmHealthCheckInProgress = false; }
    }

    private void SetLlmStatus(PlannerRuntimeStatus status)
    {
        LlmStatusText.Text = $"LLM：{status.Message}";
        LlmStatusDot.Fill = new SolidColorBrush(status.Phase == PlannerRuntimePhase.Ready
            ? System.Windows.Media.Color.FromRgb(127, 166, 106)
            : status.Phase == PlannerRuntimePhase.Error
                ? System.Windows.Media.Color.FromRgb(190, 82, 72)
                : System.Windows.Media.Color.FromRgb(211, 162, 76));
    }

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
        => ToggleOverlay();

    private void ToggleOverlay()
    {
        if (_overlay is not null) { _overlay.Close(); return; }
        try
        {
            _settings = ReadSettings();
            _store.Save(_settings);
            var profilePath = ResolveProfilePath(_settings.HudProfilePath);
            _overlay = new OverlayWindow(
                profilePath,
                _settings,
                _mouseCapability,
                _settings.EnableSessionRecording ? SqliteSessionRepository.CreateDefault() : null,
                _settings.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null,
                _sharedPlanner)
            { Opacity = _settings.OverlayOpacity, Owner = this };
            _overlay.CoachUpdated += OnCoachUpdated;
            _overlay.Closed += async (_, _) =>
            {
                _overlay = null;
                OverlayButton.Content = "啟動 Overlay";
                _trayOverlayItem.Text = "啟動 Overlay";
                await RefreshHistoryAsync();
            };
            _overlay.Show();
            OverlayButton.Content = "停止 Overlay";
            _trayOverlayItem.Text = "停止 Overlay";
            HideToTray();
        }
        catch (Exception exception) { MessageText.Text = $"無法啟動 Overlay：{exception.Message}"; }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit) return;
        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        WindowState = WindowState.Normal;
    }

    private void ShowDashboard()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        PrepareForSystemExit();
        _overlay?.Close();
        Close();
    }

    internal void PrepareForSystemExit() => _allowExit = true;

    private AppSettings ReadSettings()
    {
        var intervalText = (ScanIntervalCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var settings = new AppSettings
        {
            HudProfilePath = ProfilePathText.Text.Trim(),
            OverlayOpacity = OpacitySlider.Value,
            ScanIntervalMilliseconds = int.TryParse(intervalText, out var interval) ? interval : 500,
            EnableSessionRecording = SessionRecordingCheck.IsChecked == true,
            EnableLocalDiagnostics = LocalDiagnosticsCheck.IsChecked == true,
            EnableAutomationInput = AutomationInputCheck.IsChecked == true,
            GameHotKeyProfilePath = _settings.GameHotKeyProfilePath,
            EnableGameKeyboardInput = _settings.EnableGameKeyboardInput,
            TargetAge = Enum.TryParse<GameAge>((TargetAgeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var targetAge)
                ? targetAge : GameAge.Castle,
            AutomationStartHotKey = AutomationStartHotKeyText.Text.Trim(),
            AutomationStopHotKey = AutomationStopHotKeyText.Text.Trim(),
            EnableLocalPlanning = _settings.EnableLocalPlanning,
            LlamaRuntimePath = LlamaRuntimePathText.Text.Trim(),
            LlmModelPath = LlmModelPathText.Text.Trim(),
            VisionProjectorPath = VisionProjectorPathText.Text.Trim(),
            LlmBackend = _settings.LlmBackend,
            LlmPort = _settings.LlmPort,
            LlmContextSize = _settings.LlmContextSize,
            LlmGpuLayers = _settings.LlmGpuLayers,
            LlmPlanningTimeoutSeconds = _settings.LlmPlanningTimeoutSeconds,
        };
        settings.Validate();
        return settings;
    }

    private async void OnRefreshHistory(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();

    private async void OnExportDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            await _sessions.InitializeAsync();
            var sessions = await _sessions.GetRecentSessionsAsync(20);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "匯出 AgePilot 診斷資料",
                Filter = "JSON 檔案 (*.json)|*.json",
                FileName = $"agepilot-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true) return;

            var payload = new
            {
                schemaVersion = 1,
                exportedAt = DateTimeOffset.Now,
                appVersion = typeof(DashboardWindow).Assembly.GetName().Version?.ToString(),
                environment = new { os = Environment.OSVersion.VersionString, runtime = Environment.Version.ToString(), is64Bit = Environment.Is64BitProcess },
                settings = new
                {
                    _settings.HudProfilePath,
                    _settings.ScanIntervalMilliseconds,
                    _settings.EnableSessionRecording,
                    _settings.EnableLocalDiagnostics,
                    _settings.AutomationStartHotKey,
                    _settings.AutomationStopHotKey,
                    inputMode = "mouse-only",
                },
                liveDiagnostics = DiagnosticsText.Text,
                sessions,
                privacy = "不含遊戲截圖、按鍵內容、帳號名稱或網路遙測。",
            };
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            MessageText.Text = $"診斷資料已匯出：{dialog.FileName}";
        }
        catch (Exception exception) { MessageText.Text = $"診斷匯出失敗：{exception.Message}"; }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            await _sessions.InitializeAsync();
            var sessions = await _sessions.GetRecentSessionsAsync(5);
            RecentSessionsText.Text = sessions.Count == 0
                ? "尚無對局紀錄。"
                : string.Join(Environment.NewLine, sessions.Select(FormatSession));
        }
        catch (Exception exception)
        {
            RecentSessionsText.Text = $"無法讀取對局紀錄：{exception.Message}";
        }
    }

    private static string FormatSession(AgePilot.Core.Persistence.SessionSummary session)
    {
        var end = session.EndedAt ?? DateTimeOffset.Now;
        var duration = end - session.StartedAt;
        var state = session.EndedAt is null ? "進行中" : $"{duration:hh\\:mm\\:ss}";
        var peaks = $"峰值 F{FormatPeak(session.PeakFood)} W{FormatPeak(session.PeakWood)} G{FormatPeak(session.PeakGold)} P{FormatPeak(session.PeakPopulation)}";
        return $"{session.StartedAt.LocalDateTime:MM/dd HH:mm}  {state}  狀態 {session.SnapshotCount}  建議 {session.RecommendationCount}{Environment.NewLine}  {peaks}";
    }

    private static string FormatPeak(int? value) => value?.ToString() ?? "—";

    private static string ResolveProfilePath(string path) => Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(path, AppContext.BaseDirectory);

    private void OnCoachUpdated(object? sender, LiveCoachUpdate update)
    {
        if (update.LlmStatus is not null) SetLlmStatus(update.LlmStatus);
        var rules = update.Recommendations.Count == 0
            ? "無 active rule"
            : string.Join(", ", update.Recommendations.Select(item => $"{item.Id}:{item.Title}"));
        DiagnosticsText.Text = update.IsConnected
            ? $"{update.Lifecycle} · OCR health {update.VisionConfidence:P0} · unavailable {update.UnavailableFields}/6 · latency {update.AnalysisLatency.TotalMilliseconds:F0} ms{Environment.NewLine}{rules}"
            : $"{update.Lifecycle} · {update.Status}";
    }
}
