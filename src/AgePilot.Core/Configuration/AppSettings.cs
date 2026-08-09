using System.IO;

namespace AgePilot.Core.Configuration;

public sealed class AppSettings
{
    public string HudProfilePath { get; set; } = "config/hud/aoe2de-zh-tw-2560x1440-50.json";
    public double OverlayOpacity { get; set; } = 0.93;
    public int ScanIntervalMilliseconds { get; set; } = 500;
    public bool EnableSessionRecording { get; set; } = true;
    public bool EnableLocalDiagnostics { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HudProfilePath)) throw new InvalidDataException("HUD profile path is required.");
        if (OverlayOpacity is < 0.4 or > 1) throw new InvalidDataException("Overlay opacity must be between 0.4 and 1.0.");
        if (ScanIntervalMilliseconds is < 250 or > 5000) throw new InvalidDataException("Scan interval must be between 250 and 5000 milliseconds.");
    }
}
