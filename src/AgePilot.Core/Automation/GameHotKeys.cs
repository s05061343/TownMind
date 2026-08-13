using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgePilot.Core.Automation;

/// <summary>單一遊戲按鍵，可帶 Ctrl／Shift／Alt 修飾鍵（例如 AOE2 的 Shift+. 選所有閒置村民）。</summary>
public sealed record GameKeyChord(IReadOnlyList<string> Modifiers, string Key)
{
    public override string ToString() =>
        Modifiers.Count == 0 ? Key : $"{string.Join("+", Modifiers)}+{Key}";
}

/// <summary>
/// 遊戲內按鍵序列。與 <see cref="GlobalHotKeyParser"/> 不同：AgePilot 自身的全域熱鍵必須帶修飾鍵，
/// 遊戲快捷鍵則多半是單一字母（例如選城鎮中心的 H），因此需要獨立的解析規則。
/// </summary>
public static class GameKeySequenceParser
{
    /// <summary>
    /// 序列分隔符。不可以用逗號——',' 本身就是 AOE2 的按鍵（Go to Next Idle Military Unit），
    /// 用逗號分隔會讓該鍵永遠無法表達。
    /// </summary>
    private const char StepSeparator = '>';

    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enter", "Escape", "Space", "Tab", "Delete", "Backspace",
        "Up", "Down", "Left", "Right", "Home", "End", "PageUp", "PageDown",
    };

    /// <summary>把 "H"、"Shift+." 或 "H&gt;Q" 解析成依序送出的按鍵清單。</summary>
    public static IReadOnlyList<GameKeyChord> Parse(string sequence)
    {
        if (string.IsNullOrWhiteSpace(sequence))
            throw new InvalidDataException("遊戲按鍵序列不可為空。");
        var steps = sequence.Split(StepSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (steps.Length == 0)
            throw new InvalidDataException($"遊戲按鍵序列不可為空：{sequence}");
        return steps.Select(step => ParseChord(step, sequence)).ToArray();
    }

    private static GameKeyChord ParseChord(string value, string sequence)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new InvalidDataException($"按鍵組合不可為空：{sequence}");
        var key = parts[^1];
        if (!IsSupportedKey(key))
            throw new InvalidDataException($"不支援的遊戲按鍵：{key}（序列 {sequence}）");
        var modifiers = parts[..^1];
        foreach (var modifier in modifiers)
        {
            if (!IsModifier(modifier))
                throw new InvalidDataException($"不支援的修飾鍵：{modifier}（序列 {sequence}）");
        }
        return new(modifiers, key);
    }

    public static bool IsModifier(string key) =>
        key.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Alt", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        // 修飾鍵不能單獨當主鍵，否則 "Shift" 會被誤判成合法序列。
        if (IsModifier(key)) return false;
        if (key.Length == 1 && (char.IsLetterOrDigit(key[0]) ||
            key[0] is '.' or ',' or '-' or '=' or '[' or ']' or ';' or '/' or '\'' or '\\')) return true;
        if (NamedKeys.Contains(key)) return true;
        return key.Length is 2 or 3 && key[0] is 'F' or 'f' &&
               int.TryParse(key[1..], out var number) && number is >= 1 and <= 24;
    }
}

/// <summary>
/// 遊戲快捷鍵綁定。AOE2 DE 允許使用者自訂鍵位，因此一律由設定檔提供，不硬編碼。
/// 每個設定項對應遊戲快捷鍵清單中的一筆，讓使用者可以逐項對照驗證；
/// 需要多個按鍵才能完成的動作由 <see cref="GameActionRegistry"/> 組合，不寫成複合序列。
/// 依 ADR 0015，<see cref="Verified"/> 為 false 時不得用於送出輸入。
/// </summary>
public sealed class GameHotKeyBindings
{
    public required string Id { get; init; }

    public string Description { get; init; } = "";

    /// <summary>使用者是否已對照自己的 AOE2 DE 設定逐項確認。未確認前不得送出輸入。</summary>
    public bool Verified { get; init; }

    public required Dictionary<string, string> Keys { get; init; }

    public static GameHotKeyBindings Empty(string id = "empty") =>
        new() { Id = id, Keys = [] };

    public bool TryGetKeys(string name, out IReadOnlyList<GameKeyChord> keys, out string blockedReason)
    {
        keys = [];
        if (!Keys.TryGetValue(name, out var sequence) || string.IsNullOrWhiteSpace(sequence))
        { blockedReason = $"快捷鍵綁定缺少「{name}」"; return false; }
        try { keys = GameKeySequenceParser.Parse(sequence); }
        catch (InvalidDataException exception) { blockedReason = exception.Message; return false; }
        blockedReason = "";
        return true;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new InvalidDataException("遊戲快捷鍵設定缺少 id。");
        foreach (var (name, sequence) in Keys)
        {
            try { _ = GameKeySequenceParser.Parse(sequence); }
            catch (InvalidDataException exception)
            { throw new InvalidDataException($"快捷鍵「{name}」無效：{exception.Message}"); }
        }
    }
}

public static class GameHotKeyBindingsLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GameHotKeyBindings Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到遊戲快捷鍵設定檔", path);
        var bindings = JsonSerializer.Deserialize<GameHotKeyBindings>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"遊戲快捷鍵設定檔無法解析：{path}");
        bindings.Validate();
        return bindings;
    }
}
