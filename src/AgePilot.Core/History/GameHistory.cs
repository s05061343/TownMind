namespace AgePilot.Core.History;

public sealed class GameHistory(int capacity = 120)
{
    private readonly Queue<GameSnapshot> _snapshots = new();

    public IReadOnlyCollection<GameSnapshot> Snapshots => _snapshots;

    public void Add(GameState state, DateTimeOffset capturedAt)
    {
        _snapshots.Enqueue(new GameSnapshot(capturedAt, state));
        while (_snapshots.Count > capacity)
        {
            _snapshots.Dequeue();
        }
    }
}

public sealed record GameSnapshot(DateTimeOffset CapturedAt, GameState State);
