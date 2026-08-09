namespace AgePilot.Vision.Capture;

public interface IFrameCapture
{
    ValueTask<CapturedFrame> CaptureAsync(GameWindow window, CancellationToken cancellationToken);
}

public sealed record CapturedFrame(
    int Width,
    int Height,
    ReadOnlyMemory<byte> BgraPixels,
    DateTimeOffset CapturedAt);
