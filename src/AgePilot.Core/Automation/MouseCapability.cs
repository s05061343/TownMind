namespace AgePilot.Core.Automation;

public readonly record struct MousePoint(int X, int Y);

public sealed record MouseProbeResult(
    bool Success,
    string Status,
    nint WindowHandle = default,
    MousePoint? Original = null,
    MousePoint? Target = null,
    MousePoint? Observed = null,
    MousePoint? Restored = null);

public interface IMouseProbeBackend
{
    nint CurrentGameWindowHandle { get; }
    bool TryGetCursor(out MousePoint point, out string status);
    bool TryPrepareProbe(MousePoint original, out nint windowHandle, out MousePoint target, out string status);
    bool TrySetCursor(MousePoint point, out string status);
}

public sealed class MouseCapabilitySession
{
    private nint _verifiedWindow;

    public bool IsVerified => _verifiedWindow != nint.Zero;
    public bool IsValidFor(nint windowHandle) => windowHandle != nint.Zero && windowHandle == _verifiedWindow;

    public MouseProbeResult Run(IMouseProbeBackend backend)
    {
        _verifiedWindow = nint.Zero;
        if (!backend.TryGetCursor(out var original, out var status)) return new(false, status);
        if (!backend.TryPrepareProbe(original, out var window, out var target, out status))
            return new(false, status, Original: original);

        MousePoint? observed = null;
        MousePoint? restored = null;
        try
        {
            if (!backend.TrySetCursor(target, out status))
                return new(false, status, window, original, target);
            if (!backend.TryGetCursor(out var actual, out status) || !Near(actual, target))
                return new(false, $"游標移動讀回失敗：目標 {target.X},{target.Y}，實際 {actual.X},{actual.Y}", window, original, target, actual);
            observed = actual;
            Thread.Sleep(140);
        }
        finally
        {
            if (backend.TrySetCursor(original, out _) && backend.TryGetCursor(out var actualRestore, out _)) restored = actualRestore;
        }

        if (restored is not { } restoredPoint || !Near(restoredPoint, original))
            return new(false, "游標已移動，但無法確認已恢復原始位置", window, original, target, observed, restored);

        _verifiedWindow = window;
        return new(true,
            $"滑鼠測試通過：{original.X},{original.Y} → {observed!.Value.X},{observed.Value.Y} → {restoredPoint.X},{restoredPoint.Y}",
            window, original, target, observed, restoredPoint);
    }

    public void Reset() => _verifiedWindow = nint.Zero;

    private static bool Near(MousePoint actual, MousePoint expected) =>
        Math.Abs(actual.X - expected.X) <= 2 && Math.Abs(actual.Y - expected.Y) <= 2;
}
