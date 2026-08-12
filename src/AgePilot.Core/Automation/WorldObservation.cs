namespace AgePilot.Core.Automation;

public enum WorldTargetKind
{
    Food,
    Wood,
    Gold,
    Stone,
    OpenBuildArea,
}

public enum WorldTargetEvidence
{
    VisualCandidate,
    CrossFrameStable,
    Verified,
}

public sealed record WorldTarget(
    WorldTargetKind Kind,
    double X,
    double Y,
    double Confidence,
    WorldTargetEvidence Evidence = WorldTargetEvidence.VisualCandidate,
    int ConsistentFrames = 1)
{
    public bool IsActionable =>
        Evidence == WorldTargetEvidence.Verified &&
        ConsistentFrames >= 3 &&
        Confidence >= 0.9 &&
        X is > 0 and < 1 &&
        Y is > 0 and < 1;
}

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

    public WorldTarget? BestActionable(WorldTargetKind kind) => Targets
        .Where(target => target.Kind == kind && target.IsActionable)
        .OrderByDescending(target => target.Confidence)
        .FirstOrDefault();
}
