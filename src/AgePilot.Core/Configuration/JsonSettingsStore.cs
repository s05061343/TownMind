using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgePilot.Core.Configuration;

public sealed class JsonSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Path { get; } = path;

    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        var json = File.ReadAllText(Path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Options)
            ?? throw new InvalidDataException("Settings file is empty or invalid.");
        settings.Validate();
        RemoveLegacyLiveTraceSetting(json);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Validate();
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, Options));
    }

    public static JsonSettingsStore CreateDefault() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgePilot", "settings.json"));

    private void RemoveLegacyLiveTraceSetting(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) return;
        var key = root.Select(item => item.Key)
            .FirstOrDefault(item => item.Equals("EnableLiveTrace", StringComparison.OrdinalIgnoreCase));
        if (key is null || !root.Remove(key)) return;
        File.WriteAllText(Path, root.ToJsonString(Options));
    }
}
