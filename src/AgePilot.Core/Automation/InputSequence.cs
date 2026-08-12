using System.IO;

namespace AgePilot.Core.Automation;

public sealed record GlobalHotKey(IReadOnlyList<string> Keys);

public static class GlobalHotKeyParser
{
    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "Shift", "Alt", "Enter", "Escape", "Space", "Tab",
        "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown",
    };

    public static GlobalHotKey Parse(string gesture)
    {
        var chord = ParseChord(gesture);
        if (chord.Keys.Count < 2 || !chord.Keys.Take(chord.Keys.Count - 1).All(IsModifier))
        {
            throw new InvalidDataException($"全域熱鍵必須包含修飾鍵與一個按鍵：{gesture}");
        }
        if (IsModifier(chord.Keys[^1])) throw new InvalidDataException($"全域熱鍵缺少主要按鍵：{gesture}");
        return chord;
    }

    public static bool IsModifier(string key) =>
        key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Alt", StringComparison.OrdinalIgnoreCase);

    private static GlobalHotKey ParseChord(string value)
    {
        var keys = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0 || keys.Any(key => !IsSupportedKey(key)))
        {
            throw new InvalidDataException($"不支援的按鍵組合：{value}");
        }
        return new GlobalHotKey(keys);
    }

    private static bool IsSupportedKey(string key)
    {
        if (key.Length == 1 && (char.IsLetterOrDigit(key[0]) || key[0] is '.' or '-' or '=')) return true;
        if (NamedKeys.Contains(key)) return true;
        return key.Length is 2 or 3 && key[0] is 'F' or 'f' &&
               int.TryParse(key[1..], out var number) && number is >= 1 and <= 24;
    }
}
