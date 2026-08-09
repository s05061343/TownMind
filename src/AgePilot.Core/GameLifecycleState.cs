namespace AgePilot.Core;

public enum GameLifecycleState
{
    GameNotFound,
    GameDetected,
    GameLoading,
    GameActive,
    GamePaused,
    GameUnavailable,
    GameEnded,
}

public sealed class GameLifecycleTracker
{
    private bool _hasObservedGame;
    private bool _endedReported;

    public GameLifecycleState ObserveWindow(bool found)
    {
        if (found)
        {
            _hasObservedGame = true;
            _endedReported = false;
            return GameLifecycleState.GameDetected;
        }

        if (_hasObservedGame && !_endedReported)
        {
            _endedReported = true;
            return GameLifecycleState.GameEnded;
        }

        return GameLifecycleState.GameNotFound;
    }

    public GameLifecycleState ObserveFrame(bool pauseMenuVisible, int usableFieldCount)
    {
        _hasObservedGame = true;
        _endedReported = false;
        if (pauseMenuVisible) return GameLifecycleState.GamePaused;
        return usableFieldCount > 0 ? GameLifecycleState.GameActive : GameLifecycleState.GameLoading;
    }

    public GameLifecycleState ObserveFailure() => GameLifecycleState.GameUnavailable;
}
