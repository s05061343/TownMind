using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.Profiles;

public sealed class HudProfile
{
    public required string Id { get; init; }

    public required string Language { get; init; }

    public required string DisplayMode { get; init; }

    public required int CalibrationWidth { get; init; }

    public required int CalibrationHeight { get; init; }

    public required int HudScalePercent { get; init; }

    public required Dictionary<HudField, NormalizedRect> Regions { get; init; }

    public NormalizedRect? AgeRegion { get; init; }

    public NormalizedRect? PauseMenuRegion { get; init; }

    public NormalizedRect? MinimapRegion { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidDataException("HUD profile id is required.");
        }

        if (CalibrationWidth <= 0 || CalibrationHeight <= 0)
        {
            throw new InvalidDataException("Calibration dimensions must be positive.");
        }

        if (HudScalePercent <= 0)
        {
            throw new InvalidDataException("HUD scale must be positive.");
        }

        foreach (var field in Enum.GetValues<HudField>())
        {
            if (!Regions.TryGetValue(field, out var region))
            {
                throw new InvalidDataException($"HUD profile is missing required field '{field}'.");
            }

            region.Validate();
        }

        AgeRegion?.Validate();
        PauseMenuRegion?.Validate();
        MinimapRegion?.Validate();
    }
}
