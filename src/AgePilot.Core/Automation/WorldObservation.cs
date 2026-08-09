namespace AgePilot.Core.Automation;

public enum WorldTargetKind
{
    Food,
    Wood,
    Gold,
    Stone,
    OpenBuildArea,
}

public sealed record WorldTarget(WorldTargetKind Kind, double X, double Y, double Confidence);

public sealed record WorldObservation(
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<WorldTarget> Targets,
    double Confidence)
{
    public WorldTarget? Best(WorldTargetKind kind) => Targets
        .Where(target => target.Kind == kind)
        .OrderByDescending(target => target.Confidence)
        .FirstOrDefault();
}
