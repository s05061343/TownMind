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
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;
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

    /// <summary>
    /// 依序執行具名動作的程序步驟（ADR 0015）。任一步失敗即中止並回報，不繼續送出後續步驟——
    /// 半套序列（例如選了城鎮中心卻沒點到按鈕）會讓遊戲停在未預期狀態。
    /// </summary>
    public bool TryExecuteProcedure(GameActionProcedure procedure, NormalizedRect minimap, NormalizedRect commandGrid,
        int gridRows, int gridColumns, out string status)
    {
        var performed = new List<string>();
        foreach (var step in procedure.Steps)
        {
            switch (step.Kind)
            {
                case ProcedureStepKind.GameKey:
                    if (step.Keys is null || step.Keys.Count == 0)
                    { status = "程序步驟缺少按鍵"; return false; }
                    if (!TrySendKeys(step.Keys, out status)) return false;
                    performed.Add(status);
                    break;

                case ProcedureStepKind.CommandGridClick:
                    var click = new VisualToolAction(VisualToolKind.LeftClick, VisualCoordinateSpace.CommandGrid,
                        procedure.Description, Row: step.Row, Column: step.Column);
                    if (!MouseCoordinateMapper.TryResolve(click, minimap, commandGrid, gridRows, gridColumns,
                            out var x, out var y, out status)) return false;
                    if (!TryClick(x, y, rightClick: false, out status)) return false;
                    performed.Add(status);
                    break;

                case ProcedureStepKind.Delay:
                    Thread.Sleep(Math.Clamp(step.DelayMs, 0, 2000));
                    break;

                default:
                    status = $"未知的程序步驟：{step.Kind}";
                    return false;
            }
        }
        status = performed.Count == 0 ? "程序無需送出輸入" : string.Join("；", performed);
        return true;
    }

    /// <summary>
    /// 送出遊戲按鍵。使用 KEYEVENTF_SCANCODE 而非虛擬鍵碼——許多 DirectInput 遊戲會忽略
    /// 以虛擬鍵碼送出的 SendInput 事件。送鍵前沿用與滑鼠相同的前景視窗檢查。
    /// </summary>
    public bool TrySendKeys(IReadOnlyList<GameKeyChord> chords, out string status)
    {
        if (!TryFocusGame(out _, out status)) return false;
        foreach (var chord in chords)
        {
            if (!TryBuildChordInputs(chord, out var inputs, out status)) return false;
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            if (sent != inputs.Length)
            { status = $"按鍵 {chord} 送出失敗：{sent}/{inputs.Length}，Win32={Marshal.GetLastWin32Error()}"; return false; }
            Thread.Sleep(30);
        }
        status = $"已送出按鍵 {string.Join(" ", chords)}";
        return true;
    }

    /// <summary>修飾鍵 down → 主鍵 down → 主鍵 up → 修飾鍵反序 up。</summary>
    private static bool TryBuildChordInputs(GameKeyChord chord, out Input[] inputs, out string status)
    {
        inputs = [];
        var modifierScanCodes = new List<ushort>();
        foreach (var modifier in chord.Modifiers)
        {
            if (!TryResolveVirtualKey(modifier, out var modifierKey) || !TryScanCode(modifierKey, out var modifierScan))
            { status = $"無法解析修飾鍵：{modifier}"; return false; }
            modifierScanCodes.Add(modifierScan);
        }
        if (!TryResolveVirtualKey(chord.Key, out var virtualKey))
        { status = $"不支援的遊戲按鍵：{chord.Key}"; return false; }
        if (!TryScanCode(virtualKey, out var scanCode))
        { status = $"無法取得按鍵掃描碼：{chord.Key}"; return false; }

        var extended = IsExtendedKey(virtualKey) ? KeyEventExtended : 0;
        var sequence = new List<Input>();
        foreach (var modifierScan in modifierScanCodes)
            sequence.Add(KeyboardInput(modifierScan, KeyEventScanCode));
        sequence.Add(KeyboardInput(scanCode, KeyEventScanCode | extended));
        sequence.Add(KeyboardInput(scanCode, KeyEventScanCode | KeyEventKeyUp | extended));
        for (var index = modifierScanCodes.Count - 1; index >= 0; index--)
            sequence.Add(KeyboardInput(modifierScanCodes[index], KeyEventScanCode | KeyEventKeyUp));

        inputs = sequence.ToArray();
        status = "";
        return true;
    }

    private static bool TryScanCode(ushort virtualKey, out ushort scanCode)
    {
        scanCode = (ushort)MapVirtualKey(virtualKey, 0);
        return scanCode != 0;
    }

    private static bool TryResolveVirtualKey(string key, out ushort virtualKey)
    {
        virtualKey = 0;
        if (key.Length == 1)
        {
            var character = char.ToUpperInvariant(key[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9') { virtualKey = character; return true; }
            virtualKey = character switch
            {
                '.' => 0xBE, ',' => 0xBC, '-' => 0xBD, '=' => 0xBB, '[' => 0xDB, ']' => 0xDD,
                ';' => 0xBA, '/' => 0xBF, '\'' => 0xDE, '\\' => 0xDC, _ => 0,
            };
            return virtualKey != 0;
        }
        virtualKey = key.ToUpperInvariant() switch
        {
            "CTRL" => 0x11, "SHIFT" => 0x10, "ALT" => 0x12,
            "ENTER" => 0x0D, "ESCAPE" => 0x1B, "SPACE" => 0x20, "TAB" => 0x09, "DELETE" => 0x2E,
            "BACKSPACE" => 0x08,
            "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
            "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
            _ => 0,
        };
        if (virtualKey != 0) return true;
        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' &&
            int.TryParse(key[1..], out var number) && number is >= 1 and <= 24)
        { virtualKey = (ushort)(0x70 + number - 1); return true; }
        return false;
    }

    private static bool IsExtendedKey(ushort virtualKey) =>
        virtualKey is 0x2E or 0x26 or 0x28 or 0x25 or 0x27 or 0x24 or 0x23 or 0x21 or 0x22;

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

    /// <summary>取得 AOE2 視窗並確保它在前景。滑鼠與鍵盤送出前都必須通過這道檢查。</summary>
    private bool TryFocusGame(out GameWindow game, out string status)
    {
        game = _locator.Find()!;
        if (game is null) { status = "找不到 AOE2 視窗"; return false; }
        if (GetForegroundWindow() != game.Handle)
        {
            _ = SetForegroundWindow(game.Handle);
            Thread.Sleep(80);
        }
        if (GetForegroundWindow() != game.Handle) { status = "AOE2 不是前景視窗"; return false; }
        status = "AOE2 已在前景";
        return true;
    }

    private bool TryMove(double normalizedX, double normalizedY, out GameWindow game, out string status)
    {
        if (!TryFocusGame(out game, out status)) return false;
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

    private static Input KeyboardInput(ushort scanCode, uint flags) =>
        new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInputData { ScanCode = scanCode, Flags = flags } } };

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInputData { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInputData { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out WindowRect bounds);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
}
