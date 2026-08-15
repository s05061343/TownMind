using AgePilot.Core.Planning;

namespace AgePilot.Core.Configuration;

public sealed record CompletionTokenBudget(int TargetMedian, int PromotionCeiling, int HardCap);

public sealed record GamePlanContract(
    string Id,
    int Revision,
    string DisplayName,
    bool IsCompact,
    IReadOnlyDictionary<PlanUpdateScope, CompletionTokenBudget> CompletionBudgets);

/// <summary>
/// Versioned wire contracts. Production remains on v1 until compact-v2 passes the paired contract gate.
/// Image composition is intentionally configured separately by <see cref="VlmPipelinePresetCatalog"/>.
/// </summary>
public static class GamePlanContractCatalog
{
    public static readonly GamePlanContract Legacy = new(
        "legacy-v1", 1, "Legacy GamePlan v1", false,
        new Dictionary<PlanUpdateScope, CompletionTokenBudget>
        {
            [PlanUpdateScope.Minor] = new(1024, 1024, 1024),
            [PlanUpdateScope.Medium] = new(1024, 1024, 1024),
            [PlanUpdateScope.Major] = new(1024, 1024, 1024),
        });

    public static readonly GamePlanContract CompactV2 = new(
        "compact-v2", 2, "Compact GamePlan v2", true,
        new Dictionary<PlanUpdateScope, CompletionTokenBudget>
        {
            [PlanUpdateScope.Minor] = new(64, 96, 160),
            [PlanUpdateScope.Medium] = new(96, 128, 192),
            [PlanUpdateScope.Major] = new(96, 200, 256),
        });

    private static readonly IReadOnlyDictionary<string, GamePlanContract> Contracts =
        new[] { Legacy, CompactV2 }.ToDictionary(item => item.Id, StringComparer.Ordinal);

    public static IReadOnlyCollection<GamePlanContract> All { get; } = Contracts.Values.ToArray();

    public static GamePlanContract Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !Contracts.TryGetValue(id, out var contract))
            throw new InvalidDataException($"未知或未驗證的 GamePlan contract：{id}");
        return contract;
    }
}
