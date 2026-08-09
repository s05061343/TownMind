using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgePilot.Vision.Profiles;

public static class HudProfileLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static HudProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        var profile = JsonSerializer.Deserialize<HudProfile>(stream, Options)
            ?? throw new InvalidDataException("HUD profile is empty or invalid.");

        profile.Validate();
        return profile;
    }
}
