namespace AgePilot.Vision.Capture;

public interface IGameWindowLocator
{
    GameWindow? Find();
}

public sealed record GameWindow(nint Handle, string Title, int ProcessId);
