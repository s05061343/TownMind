using System.Runtime.InteropServices;
using AgePilot.Core.Planning;
using AgePilot.Vision.Capture;
using AgePilot.Vision.Geometry;

namespace AgePilot.App;

internal sealed class WindowsInputSender
{
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private readonly WindowsGameWindowLocator _locator = new();

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
        status = $"游標已移至 {x},{y}";
        return true;
    }

    private static Input MouseInput(uint flags) => new() { Type = 0, Data = new InputUnion { Mouse = new MouseInputData { Flags = flags } } };

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public MouseInputData Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInputData { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
}
