using AgePilot.Core.Configuration;

namespace AgePilot.Core.Automation;

public enum AutomationActionKind
{
    None,
    QueueVillager,
    MilitarySequence,
}

public sealed record AutomationDecision(AutomationActionKind Kind, string Reason);

public static class AutomationPolicy
{
    public static AutomationDecision DecideEconomy(GameState? state)
    {
        if (state?.Food?.IsUsable != true ||
            state.Population?.IsUsable != true ||
            state.PopulationCap?.IsUsable != true)
        {
            return new(AutomationActionKind.None, "等待可靠的食物與人口資料");
        }

        if (state.Population.Value >= state.PopulationCap.Value)
        {
            return new(AutomationActionKind.None, "人口空間不足，請手動建造房屋");
        }

        return state.Food.Value >= 50
            ? new(AutomationActionKind.QueueVillager, "食物與人口空間足夠")
            : new(AutomationActionKind.None, "食物不足 50");
    }

    public static IReadOnlyList<string> EnabledMilitarySequences(AppSettings settings) =>
        new[]
        {
            settings.BarracksProductionSequence,
            settings.ArcheryRangeProductionSequence,
            settings.StableProductionSequence,
        }.Where(sequence => !string.IsNullOrWhiteSpace(sequence)).ToArray();
}
