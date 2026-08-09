namespace AgePilot.Core.Observations;

public sealed record ObservedValue<T>(
    T? Value,
    double Confidence,
    DateTimeOffset ObservedAt,
    ObservationStatus Status)
    where T : struct
{
    public bool IsUsable =>
        Value is not null &&
        Status == ObservationStatus.Confirmed &&
        Confidence is >= 0 and <= 1;

    public static ObservedValue<T> Unavailable(DateTimeOffset observedAt) =>
        new(default, 0, observedAt, ObservationStatus.Unavailable);
}
