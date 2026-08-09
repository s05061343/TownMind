using System.Windows;
using System.Windows.Input;
using AgePilot.Core;

namespace AgePilot.App;

public partial class OverlayWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly LiveCoachService _coach;
    private Task? _monitorTask;

    public OverlayWindow(string profilePath)
    {
        InitializeComponent();
        _coach = new LiveCoachService(profilePath);
        Loaded += OnLoaded;
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
        StatusText.Text = update.Status;
        UpdateResources(update.State);

        var advice = update.Recommendations.FirstOrDefault();
        AdviceTitle.Text = advice?.Title ?? (update.IsConnected ? "目前狀態穩定" : "等待遊戲連線");
        AdviceDescription.Text = advice?.Description ??
            (update.IsConnected ? "沒有需要立即提醒的事項。" : "啟動 AOE2 並進入對局後會自動開始。 ");
    }).Task;

    private void UpdateResources(GameState? state)
    {
        WoodText.Text = $"🪵 {Format(state?.Wood?.Value)}";
        FoodText.Text = $"🍖 {Format(state?.Food?.Value)}";
        GoldText.Text = $"🪙 {Format(state?.Gold?.Value)}";
        StoneText.Text = $"🪨 {Format(state?.Stone?.Value)}";
        PopulationText.Text = state?.Population?.Value is { } population && state.PopulationCap?.Value is { } cap
            ? $"👤 {population}/{cap}"
            : "👤 —";
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

    private void OnClosed(object? sender, EventArgs e)
    {
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
}
