using System.IO;
using System.Text.Json;

namespace AgePilot.Core.Configuration;

public sealed class JsonSettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Path { get; } = path;

    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options)
            ?? throw new InvalidDataException("Settings file is empty or invalid.");
        settings.Validate();
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
}
