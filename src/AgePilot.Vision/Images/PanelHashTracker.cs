using System.Diagnostics;
using OpenCvSharp;
using AgePilot.Vision.Geometry;

namespace AgePilot.Vision.Images;

public sealed record PanelHashSnapshot(
    ulong? CurrentRawHash,
    ulong? CandidatePanelHash,
    ulong? StablePanelHash,
    ulong? LastAttemptedPanelHash,
    DateTimeOffset? LastAttemptedPanelAt,
    ulong? LastAcceptedPanelHash,
    DateTimeOffset? LastAcceptedPanelAt,
    bool PanelDirty,
    long HashMilliseconds);

public sealed class PanelHashTracker(
    int dirtyThreshold,
    int candidateTolerance,
    TimeSpan stableDuration,
    int minimumStableSamples)
{
    private ulong? _raw;
    private ulong? _candidate;
    private DateTimeOffset _candidateSince;
    private int _candidateSamples;
    private ulong? _stable;
    private ulong? _attempted;
    private DateTimeOffset? _attemptedAt;
    private ulong? _accepted;
    private DateTimeOffset? _acceptedAt;
    private long _lastHashMilliseconds;

    public PanelHashSnapshot Snapshot => new(_raw, _candidate, _stable, _attempted, _attemptedAt,
        _accepted, _acceptedAt, IsDirty(), _lastHashMilliseconds);

    public void Observe(ReadOnlySpan<byte> bgra, int width, int height, NormalizedRect panel, DateTimeOffset at)
    {
        var clock = Stopwatch.StartNew();
        var pixels = panel.ToPixels(width, height);
        var cropBytes = new byte[checked(pixels.Width * pixels.Height * 4)];
        var rowBytes = pixels.Width * 4;
        for (var row = 0; row < pixels.Height; row++)
        {
            var sourceOffset = checked(((pixels.Y + row) * width + pixels.X) * 4);
            bgra.Slice(sourceOffset, rowBytes).CopyTo(cropBytes.AsSpan(row * rowBytes, rowBytes));
        }
        using var crop = Mat.FromPixelData(pixels.Height, pixels.Width, MatType.CV_8UC4, cropBytes);
        var raw = ComputePerceptualHash(crop);
        clock.Stop();
        _lastHashMilliseconds = clock.ElapsedMilliseconds;
        ObserveHash(raw, at);
    }

    public void ObserveHash(ulong raw, DateTimeOffset at)
    {
        _raw = raw;
        if (_candidate is null || HammingDistance(_candidate.Value, raw) > candidateTolerance)
        {
            _candidate = raw;
            _candidateSince = at;
            _candidateSamples = 1;
            return;
        }

        _candidateSamples++;
        if (_candidateSamples >= minimumStableSamples && at - _candidateSince >= stableDuration)
            _stable = _candidate;
    }

    public void MarkAttempted(ulong hash, DateTimeOffset at)
    {
        _attempted = hash;
        _attemptedAt = at;
    }

    public void MarkAccepted(ulong hash, DateTimeOffset at)
    {
        _accepted = hash;
        _acceptedAt = at;
    }

    public bool AcceptedTtlExpired(DateTimeOffset now, TimeSpan ttl) =>
        _acceptedAt is { } acceptedAt && now - acceptedAt >= ttl;

    private bool IsDirty() => _stable is { } stable &&
        (_accepted is null || HammingDistance(stable, _accepted.Value) >= dirtyThreshold);

    public static int HammingDistance(ulong left, ulong right) => System.Numerics.BitOperations.PopCount(left ^ right);

    public static ulong ComputePerceptualHash(Mat bgraOrBgr)
    {
        using var gray = new Mat();
        if (bgraOrBgr.Channels() == 4) Cv2.CvtColor(bgraOrBgr, gray, ColorConversionCodes.BGRA2GRAY);
        else if (bgraOrBgr.Channels() == 3) Cv2.CvtColor(bgraOrBgr, gray, ColorConversionCodes.BGR2GRAY);
        else bgraOrBgr.CopyTo(gray);
        using var resized = new Mat();
        Cv2.Resize(gray, resized, new Size(32, 32), 0, 0, InterpolationFlags.Area);
        using var values = new Mat();
        resized.ConvertTo(values, MatType.CV_32F);
        using var dct = new Mat();
        Cv2.Dct(values, dct);
        var coefficients = new List<float>(63);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
            if (x != 0 || y != 0) coefficients.Add(dct.At<float>(y, x));
        var ordered = coefficients.OrderBy(value => value).ToArray();
        var median = ordered[ordered.Length / 2];
        ulong hash = 0;
        for (var index = 0; index < coefficients.Count; index++)
            if (coefficients[index] >= median) hash |= 1UL << index;
        return hash;
    }
}
