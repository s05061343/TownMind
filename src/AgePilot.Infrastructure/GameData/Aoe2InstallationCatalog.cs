using System.Security.Cryptography;
using System.Text.Json;

namespace AgePilot.Infrastructure.GameData;

public sealed record Aoe2Hotkey(string Command, string Key, bool Control, bool Shift, bool Alt)
{
    public string ToInputSequence()
    {
        var parts = new List<string>(4);
        if (Control) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(Key.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) ? Key[3..] : Key);
        return string.Join('+', parts);
    }
}

public sealed record Aoe2InstallationCatalog(
    string InstallationPath,
    string HotkeysPath,
    string HotkeysSha256,
    IReadOnlyDictionary<string, Aoe2Hotkey> DefinitiveHotkeys)
{
    public static Aoe2InstallationCatalog Load(string installationPath)
    {
        if (string.IsNullOrWhiteSpace(installationPath))
            throw new ArgumentException("AOE2 DE installation path is required.", nameof(installationPath));

        var root = Path.GetFullPath(installationPath);
        var hotkeysPath = Path.Combine(root, "resources", "_common", "dat", "hotkeys.json");
        if (!File.Exists(hotkeysPath))
            throw new FileNotFoundException("找不到 AOE2 DE hotkeys.json。", hotkeysPath);

        var bytes = File.ReadAllBytes(hotkeysPath);
        using var document = JsonDocument.Parse(bytes);
        var result = new Dictionary<string, Aoe2Hotkey>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("shared_hotkey_group_list", out var groups))
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("hotkey_list", out var hotkeys)) continue;
                foreach (var hotkey in hotkeys.EnumerateArray())
                {
                    if (!hotkey.TryGetProperty("data_name", out var nameElement) ||
                        !hotkey.TryGetProperty("defaults_list", out var defaults)) continue;
                    var selected = defaults.EnumerateArray().FirstOrDefault(item =>
                        item.TryGetProperty("name", out var profile) && profile.GetString() == "definitive");
                    if (selected.ValueKind == JsonValueKind.Undefined ||
                        !selected.TryGetProperty("key", out var keyElement)) continue;
                    var command = nameElement.GetString();
                    var key = keyElement.GetString();
                    if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(key)) continue;
                    result[command] = new Aoe2Hotkey(
                        command,
                        key,
                        selected.TryGetProperty("control", out var control) && control.GetBoolean(),
                        selected.TryGetProperty("shift", out var shift) && shift.GetBoolean(),
                        selected.TryGetProperty("alt", out var alt) && alt.GetBoolean());
                }
            }
        }

        return new(root, hotkeysPath, Convert.ToHexString(SHA256.HashData(bytes)), result);
    }
}
