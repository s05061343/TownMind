using System.Runtime.InteropServices;
using AgePilot.Core.Planning;
using AgePilot.Core.Automation;
using AgePilot.Vision.Capture;
using AgePilot.Vision.Geometry;

namespace AgePilot.App;

internal sealed class WindowsInputSender : IMouseProbeBackend
{
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private readonly WindowsGameWindowLocator _locator = new();

    public nint CurrentGameWindowHandle => _locator.Find()?.Handle ?? nint.Zero;

    public bool TryGetCursor(out MousePoint point, out string status)
    {
        if (!GetCursorPos(out var native)) { point = default; status = $"無法讀取游標位置，Win32={Marshal.GetLastWin32Error()}"; return false; }
        point = new(native.X, native.Y); status = "已讀取游標位置"; return true;
    }

    public bool TryPrepareProbe(MousePoint original, out nint windowHandle, out MousePoint target, out string status)
    {
        var game = _locator.Find();
        if (game is null) { windowHandle = nint.Zero; target = default; status = "滑鼠測試失敗：找不到 AOE2 視窗"; return false; }
        windowHandle = game.Handle;
        if (GetForegroundWindow() != game.Handle)
        {
            _ = SetForegroundWindow(game.Handle);
            Thread.Sleep(80);
        }
        if (GetForegroundWindow() != game.Handle) { target = default; status = "滑鼠測試失敗：無法將 AOE2 切到前景"; return false; }
        if (!GetWindowRect(game.Handle, out var bounds))
        { target = default; status = $"滑鼠測試失敗：無法讀取 AOE2 視窗範圍，Win32={Marshal.GetLastWin32Error()}"; return false; }
        var baseX = Math.Clamp(original.X, bounds.Left + 32, bounds.Right - 32);
        var baseY = Math.Clamp(original.Y, bounds.Top + 2, bounds.Bottom - 2);
        var targetX = baseX + 30 <= bounds.Right - 2 ? baseX + 30 : baseX - 30;
        target = new(targetX, baseY);
        status = "滑鼠測試目標已準備";
        return true;
    }

    public bool TrySetCursor(MousePoint point, out string status)
    {
        if (!SetCursorPos(point.X, point.Y)) { status = $"無法移動游標，Win32={Marshal.GetLastWin32Error()}"; return false; }
        status = $"游標已移至 {point.X},{point.Y}"; return true;
    }

    public bool TryExecute(VisualToolAction action, NormalizedRect minimap, NormalizedRect commandGrid,
        int gridRows, int gridColumns, out string status)
    {
        if (!MouseCoordinateMapper.TryResolve(action, minimap, commandGrid, gridRows, gridColumns, out var startX, out var startY, out status))
            return false;
        if (action.Tool == VisualToolKind.Drag)
        {
            if (!MouseCoordinateMapper.TryResolve(action with { X = action.EndX, Y = action.EndY }, minimap, commandGrid,
                    gridRows, gridColumns, out var endX, out var endY, out status)) return false;
            return TryDrag(startX, startY, endX, endY, out status);
        }
        return TryClick(startX, startY, action.Tool == VisualToolKind.RightClick, out status);
    }

    private bool TryClick(double normalizedX, double normalizedY, bool rightClick, out string status)
    {
        if (!TryMove(normalizedX, normalizedY, out var game, out status)) return false;
        var flags = rightClick ? new[] { MouseRightDown, MouseRightUp } : new[] { MouseLeftDown, MouseLeftUp };
        var inputs = flags.Select(flag => MouseInput(flag)).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        { status = $"滑鼠點擊失敗：{sent}/{inputs.Length}，Win32={Marshal.GetLastWin32Error()}"; return false; }
        status = $"已送出滑鼠點擊至 {normalizedX:P1},{normalizedY:P1}（視窗={game.Handle}）";
        return true;
    }

    private bool TryDrag(double startX, double startY, double endX, double endY, out string status)
    {
        if (!TryMove(startX, startY, out _, out status)) return false;
        if (SendInput(1, [MouseInput(MouseLeftDown)], Marshal.SizeOf<Input>()) != 1)
        { status = $"拖曳按下失敗，Win32={Marshal.GetLastWin32Error()}"; return false; }
        Thread.Sleep(60);
        if (!TryMove(endX, endY, out _, out status))
        { _ = SendInput(1, [MouseInput(MouseLeftUp)], Marshal.SizeOf<Input>()); return false; }
        if (SendInput(1, [MouseInput(MouseLeftUp)], Marshal.SizeOf<Input>()) != 1)
        { status = $"拖曳放開失敗，Win32={Marshal.GetLastWin32Error()}"; return false; }
        status = $"已送出拖曳 {startX:P1},{startY:P1} → {endX:P1},{endY:P1}";
        return true;
    }

    private bool TryMove(double normalizedX, double normalizedY, out GameWindow game, out string status)
    {
        game = _locator.Find()!;
        if (game is null) { status = "找不到 AOE2 視窗"; return false; }
        if (GetForegroundWindow() != game.Handle) { status = "AOE2 不是前景視窗"; return false; }
        if (!GetWindowRect(game.Handle, out var bounds))
        { status = $"無法讀取 AOE2 視窗範圍，Win32={Marshal.GetLastWin32Error()}"; return false; }
        var x = bounds.Left + (int)Math.Round((bounds.Right - bounds.Left) * normalizedX);
        var y = bounds.Top + (int)Math.Round((bounds.Bottom - bounds.Top) * normalizedY);
        if (!SetCursorPos(x, y)) { status = $"無法移動滑鼠，Win32={Marshal.GetLastWin32Error()}"; return false; }
        if (!GetCursorPos(out var actual) || Math.Abs(actual.X - x) > 2 || Math.Abs(actual.Y - y) > 2)
        { status = $"游標位置讀回不符：目標 {x},{y}，實際 {actual.X},{actual.Y}"; return false; }
        status = $"游標已移至 {x},{y}";
        return true;
    }

    private static Input MouseInput(uint flags) => new() { Type = 0, Data = new InputUnion { Mouse = new MouseInputData { Flags = flags } } };

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public MouseInputData Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInputData { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
}
