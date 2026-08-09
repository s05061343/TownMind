namespace AgePilot.Core.Automation;

public enum EconomicActionKind
{
    Wait,
    QueueVillager,
    GatherFood,
    GatherWood,
    GatherGold,
    BuildHouse,
    BuildMarket,
    BuildBlacksmith,
    AdvanceFeudal,
    AdvanceCastle,
}

public sealed record EconomicAction(
    EconomicActionKind Kind,
    string Reason,
    WorldTarget? Target = null,
    string? Confirmation = null);

public sealed class GenericEconomicPlanner
{
    public EconomicAction Decide(GameState? state, WorldObservation? world, bool marketPlanned, bool blacksmithPlanned)
    {
        if (state?.Food?.IsUsable != true || state.Wood?.IsUsable != true ||
            state.Population?.IsUsable != true || state.PopulationCap?.IsUsable != true)
            return new(EconomicActionKind.Wait, "等待可靠的食物、木材與人口資料");

        var population = state.Population.Value!.Value;
        var cap = state.PopulationCap.Value!.Value;
        var food = state.Food.Value!.Value;
        var wood = state.Wood.Value!.Value;
        var gold = state.Gold?.IsUsable == true ? state.Gold.Value.GetValueOrDefault() : 0;

        if (cap - population <= 2 && wood >= 25)
            return Targeted(EconomicActionKind.BuildHouse, "人口空間不足，建造房屋", world, WorldTargetKind.OpenBuildArea, "PopulationCapIncrease");

        if (state.Age == GameAge.Dark && population >= 21 && food >= 500)
            return new(EconomicActionKind.AdvanceFeudal, "21 人且食物足夠，升級封建時代", Confirmation: "AgeFeudal");

        if (state.Age == GameAge.Feudal && !marketPlanned && wood >= 175)
            return Targeted(EconomicActionKind.BuildMarket, "建造城堡時代前置市場", world, WorldTargetKind.OpenBuildArea, "WoodDecrease");

        if (state.Age == GameAge.Feudal && marketPlanned && !blacksmithPlanned && wood >= 150)
            return Targeted(EconomicActionKind.BuildBlacksmith, "建造城堡時代前置鐵匠鋪", world, WorldTargetKind.OpenBuildArea, "WoodDecrease");

        if (state.Age == GameAge.Feudal && marketPlanned && blacksmithPlanned && food >= 800 && gold >= 200)
            return new(EconomicActionKind.AdvanceCastle, "前置建築與資源就緒，升級城堡時代", Confirmation: "AgeCastle");

        if (food < 400)
            return Targeted(EconomicActionKind.GatherFood, "食物低於通用經濟目標", world, WorldTargetKind.Food);
        if (wood < 250)
            return Targeted(EconomicActionKind.GatherWood, "木材低於通用建設目標", world, WorldTargetKind.Wood);
        if (state.Age is GameAge.Feudal or GameAge.Castle && gold < 200)
            return Targeted(EconomicActionKind.GatherGold, "黃金低於升級目標", world, WorldTargetKind.Gold);

        return population < cap
            ? new(EconomicActionKind.QueueVillager, "維持村民生產")
            : new(EconomicActionKind.Wait, "人口已滿，等待房屋完成");
    }

    private static EconomicAction Targeted(
        EconomicActionKind action,
        string reason,
        WorldObservation? world,
        WorldTargetKind targetKind,
        string? confirmation = null)
    {
        var target = world?.Best(targetKind);
        return target is { Confidence: >= 0.55 }
            ? new(action, reason, target, confirmation)
            : new(EconomicActionKind.Wait, $"{reason}，但尚未找到可靠的{Format(targetKind)}位置");
    }

    private static string Format(WorldTargetKind kind) => kind switch
    {
        WorldTargetKind.Food => "食物",
        WorldTargetKind.Wood => "木材",
        WorldTargetKind.Gold => "黃金",
        WorldTargetKind.Stone => "石頭",
        _ => "建築空地",
    };
}
