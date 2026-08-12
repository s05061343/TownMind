namespace AgePilot.Core.Planning;

public enum PlannerRuntimePhase
{
    NotConfigured,
    Starting,
    LoadingModel,
    Ready,
    Planning,
    Error,
}

public sealed record PlannerRuntimeStatus(
    PlannerRuntimePhase Phase,
    string Message,
    string? Backend = null,
    DateTimeOffset? UpdatedAt = null)
{
    public static PlannerRuntimeStatus NotConfigured(string message = "尚未設定 LLM") =>
        new(PlannerRuntimePhase.NotConfigured, message, UpdatedAt: DateTimeOffset.UtcNow);
}

public interface IPlannerRuntimeStatusSource
{
    PlannerRuntimeStatus RuntimeStatus { get; }
}
