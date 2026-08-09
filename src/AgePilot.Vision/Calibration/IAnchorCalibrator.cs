using AgePilot.Vision.Profiles;

namespace AgePilot.Vision.Calibration;

public interface IAnchorCalibrator
{
    ValueTask<CalibrationResult> CalibrateAsync(
        ReadOnlyMemory<byte> frame,
        int width,
        int height,
        HudProfile profile,
        CancellationToken cancellationToken);
}

public sealed record CalibrationResult(bool IsSuccess, string? FailureReason);
