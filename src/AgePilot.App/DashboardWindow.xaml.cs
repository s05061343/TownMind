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
using System.ComponentModel;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AgePilot.App;

public partial class DashboardWindow : Window
{
    private readonly JsonSettingsStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SqliteSessionRepository _sessions = SqliteSessionRepository.CreateDefault();
    private AppSettings _settings;
    private OverlayWindow? _overlay;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _trayOverlayItem;
    private readonly Drawing.Icon? _applicationIcon;
    private bool _allowExit;

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
        _timer.Tick += (_, _) => RefreshGame();
        Loaded += async (_, _) => { RefreshGame(); _timer.Start(); await RefreshHistoryAsync(); };
        Closing += OnWindowClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) HideToTray(); };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _overlay?.Close();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _applicationIcon?.Dispose();
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
        AutomationStartHotKeyText.Text = _settings.AutomationStartHotKey;
        AutomationStopHotKeyText.Text = _settings.AutomationStopHotKey;
        VillagerSequenceText.Text = _settings.VillagerProductionSequence;
        MilitaryAutomationCheck.IsChecked = _settings.EnableMilitaryAutomation;
        BarracksSequenceText.Text = _settings.BarracksProductionSequence;
        ArcheryRangeSequenceText.Text = _settings.ArcheryRangeProductionSequence;
        StableSequenceText.Text = _settings.StableProductionSequence;
        IdleVillagerSequenceText.Text = _settings.IdleVillagerSelectionSequence;
        HouseSequenceText.Text = _settings.HouseBuildSequence;
        MarketSequenceText.Text = _settings.MarketBuildSequence;
        BlacksmithSequenceText.Text = _settings.BlacksmithBuildSequence;
        FeudalUpgradeSequenceText.Text = _settings.FeudalUpgradeSequence;
        CastleUpgradeSequenceText.Text = _settings.CastleUpgradeSequence;
        LlamaRuntimePathText.Text = _settings.LlamaRuntimePath;
        LlmModelPathText.Text = _settings.LlmModelPath;
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
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        try { _settings = ReadSettings(); _store.Save(_settings); MessageText.Text = $"設定已儲存至 {_store.Path}"; }
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

    private async void OnTestLlm(object sender, RoutedEventArgs e)
    {
        TestLlmButton.IsEnabled = false;
        try
        {
            var candidate = ReadSettings();
            SetLlmStatus(new(PlannerRuntimePhase.Starting, "正在啟動 llama-server 並載入模型…"));
            using var planner = new LlamaServerPlanner(candidate,
                candidate.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(190));
            SetLlmStatus(await planner.CheckReadyAsync(timeout.Token));
        }
        catch (OperationCanceledException) { SetLlmStatus(new(PlannerRuntimePhase.Error, "LLM 測試逾時")); }
        catch (Exception exception) { SetLlmStatus(new(PlannerRuntimePhase.Error, exception.Message)); }
        finally { TestLlmButton.IsEnabled = true; }
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
                _settings.EnableSessionRecording ? SqliteSessionRepository.CreateDefault() : null,
                _settings.EnableLocalDiagnostics ? LocalJsonLineLogger.CreateDefault() : null)
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
            AutomationStartHotKey = AutomationStartHotKeyText.Text.Trim(),
            AutomationStopHotKey = AutomationStopHotKeyText.Text.Trim(),
            VillagerProductionSequence = VillagerSequenceText.Text.Trim(),
            EnableMilitaryAutomation = MilitaryAutomationCheck.IsChecked == true,
            BarracksProductionSequence = BarracksSequenceText.Text.Trim(),
            ArcheryRangeProductionSequence = ArcheryRangeSequenceText.Text.Trim(),
            StableProductionSequence = StableSequenceText.Text.Trim(),
            IdleVillagerSelectionSequence = IdleVillagerSequenceText.Text.Trim(),
            HouseBuildSequence = HouseSequenceText.Text.Trim(),
            MarketBuildSequence = MarketSequenceText.Text.Trim(),
            BlacksmithBuildSequence = BlacksmithSequenceText.Text.Trim(),
            FeudalUpgradeSequence = FeudalUpgradeSequenceText.Text.Trim(),
            CastleUpgradeSequence = CastleUpgradeSequenceText.Text.Trim(),
            EconomyActionIntervalMilliseconds = _settings.EconomyActionIntervalMilliseconds,
            MilitaryActionIntervalMilliseconds = _settings.MilitaryActionIntervalMilliseconds,
            StrategicActionIntervalMilliseconds = _settings.StrategicActionIntervalMilliseconds,
            EnableLocalPlanning = _settings.EnableLocalPlanning,
            LlamaRuntimePath = LlamaRuntimePathText.Text.Trim(),
            LlmModelPath = LlmModelPathText.Text.Trim(),
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
                    _settings.EnableMilitaryAutomation,
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
