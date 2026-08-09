using System.Runtime.InteropServices;
using AgePilot.Core.Automation;
using AgePilot.Vision.Capture;

namespace AgePilot.App;

internal sealed class WindowsInputSender
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private readonly WindowsGameWindowLocator _locator = new();

    public bool TrySend(string sequence, out string status)
    {
        var game = _locator.Find();
        if (game is null)
        {
            status = "找不到 AOE2 視窗，未送出按鍵";
            return false;
        }
        if (GetForegroundWindow() != game.Handle)
        {
            status = "AOE2 不在前景，未送出按鍵";
            return false;
        }

        foreach (var chord in InputSequence.Parse(sequence))
        {
            if (GetForegroundWindow() != game.Handle)
            {
                status = "操作期間焦點離開 AOE2，已停止";
                return false;
            }
            if (!TrySendChord(chord, out status)) return false;
            Thread.Sleep(35);
        }

        status = $"已送出：{sequence}";
        return true;
    }

    public bool TrySendThenClick(string sequence, double normalizedX, double normalizedY, bool rightClick, out string status)
    {
        if (!TrySend(sequence, out status)) return false;
        return TryClick(normalizedX, normalizedY, rightClick, out status);
    }

    public bool TryClick(double normalizedX, double normalizedY, bool rightClick, out string status)
    {
        var game = _locator.Find();
        if (game is null || GetForegroundWindow() != game.Handle)
        {
            status = game is null ? "找不到 AOE2 視窗" : "AOE2 不在前景";
            return false;
        }
        if (!GetWindowRect(game.Handle, out var bounds))
        {
            status = $"無法取得遊戲位置（Win32={Marshal.GetLastWin32Error()}）";
            return false;
        }
        var x = bounds.Left + (int)Math.Round((bounds.Right - bounds.Left) * Math.Clamp(normalizedX, 0d, 1d));
        var y = bounds.Top + (int)Math.Round((bounds.Bottom - bounds.Top) * Math.Clamp(normalizedY, 0d, 1d));
        if (!SetCursorPos(x, y))
        {
            status = $"無法移動滑鼠（Win32={Marshal.GetLastWin32Error()}）";
            return false;
        }
        var flags = rightClick ? new[] { MouseRightDown, MouseRightUp } : new[] { MouseLeftDown, MouseLeftUp };
        var inputs = flags.Select(flag => new Input
        {
            Type = 0,
            Data = new InputUnion { Mouse = new MouseInputData { Flags = flag } },
        }).ToArray();
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            status = $"滑鼠輸入失敗（{sent}/{inputs.Length}, Win32={Marshal.GetLastWin32Error()}）";
            return false;
        }
        status = $"已點擊遊戲座標 {normalizedX:P0},{normalizedY:P0}";
        return true;
    }

    private static bool TrySendChord(InputChord chord, out string status)
    {
        var keys = chord.Keys.Select(ToVirtualKey).ToArray();
        var inputs = new List<Input>(keys.Length * 2);
        inputs.AddRange(keys.Select(key => KeyboardInput(key, keyUp: false)));
        inputs.AddRange(keys.Reverse().Select(key => KeyboardInput(key, keyUp: true)));
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            var error = Marshal.GetLastWin32Error();
            status = error == 5
                ? "Windows 拒絕輸入；請確認 AgePilot 與 AOE2 使用相同權限層級"
                : $"SendInput 只送出 {sent}/{inputs.Count} 個事件（Win32={error}）";
            return false;
        }
        status = "按鍵已送出";
        return true;
    }

    private static ushort ToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            var code = VkKeyScan(char.ToUpperInvariant(key[0]));
            if (code < 0) throw new InvalidOperationException($"Windows 無法轉換按鍵：{key}");
            return (ushort)(code & 0xff);
        }
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(key[1..], out var function))
            return (ushort)(0x70 + function - 1);
        return key.ToUpperInvariant() switch
        {
            "CTRL" => 0x11,
            "SHIFT" => 0x10,
            "ALT" => 0x12,
            "ENTER" => 0x0D,
            "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            _ => throw new InvalidOperationException($"不支援的按鍵：{key}"),
        };
    }

    private static Input KeyboardInput(ushort key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInputData { VirtualKey = key, Flags = keyUp ? KeyUp : 0 },
        },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public MouseInputData Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowRect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char character);
}
