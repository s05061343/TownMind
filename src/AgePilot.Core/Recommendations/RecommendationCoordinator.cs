namespace AgePilot.Core.Recommendations;

public sealed class RecommendationCoordinator
{
    private readonly HashSet<string> _dismissed = [];

    public IReadOnlyList<Recommendation> Apply(IReadOnlyList<Recommendation> active)
    {
        var activeIds = active.Select(item => item.Id).ToHashSet();
        _dismissed.RemoveWhere(id => !activeIds.Contains(id));
        return active.Where(item => !_dismissed.Contains(item.Id)).ToArray();
    }

    public void Dismiss(string recommendationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recommendationId);
        _dismissed.Add(recommendationId);
    }
}
