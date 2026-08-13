using AgePilot.Core.History;
using AgePilot.Core.Planning;

namespace AgePilot.Core.Automation;

/// <summary>送出動作當下擷取的基準線，供 <see cref="ActionOutcomeVerifier"/> 比對。</summary>
public sealed record ActionOutcomeBaseline(
    GameActionKind Kind,
    Postcondition Post,
    GameState State,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    double? GatherRate = null);

/// <summary>
/// 以 OCR 觀測判定前一動作結果（ADR 0015）。
///
/// 這一層取代了原本「詢問 VLM 前一動作是否成功」的機制。舊機制在 2026-08-13 的三場實機 session 中
/// 從未回傳過一次 Confirmed，因為模型被要求驗證的是它自己填的策略級 expectedResult，
/// 而非單一動作在遊戲中的即時後果。
/// </summary>
public static class ActionOutcomeVerifier
{
    /// <summary>
    /// 資源扣除的判定寬容度。等待視窗內採集收入會抵銷一部分扣除，OCR 也有誤差，
    /// 因此不要求看到完整成本，只要求跌幅達成本的一半以上。
    /// </summary>
    private const double SpendTolerance = 0.5;

    public static PreviousActionResult Evaluate(
        ActionOutcomeBaseline baseline,
        GameState? current,
        GameHistory? history,
        DateTimeOffset now,
        out string status)
    {
        var expired = now >= baseline.Deadline;

        switch (baseline.Post.Kind)
        {
            case PostconditionKind.None:
                status = "無需遊戲側確認";
                return PreviousActionResult.Confirmed;

            case PostconditionKind.ResourceSpent:
                return EvaluateSpend(baseline, current, expired, out status);

            case PostconditionKind.PopulationIncrease:
                return EvaluateIncrease(baseline.State.Population, current?.Population, expired, "人口", out status);

            case PostconditionKind.PopulationCapIncrease:
                return EvaluateIncrease(baseline.State.PopulationCap, current?.PopulationCap, expired, "人口上限", out status);

            case PostconditionKind.AgeAdvanced:
                return EvaluateAge(baseline, current, expired, out status);

            case PostconditionKind.GatherRateIncrease:
                return EvaluateGatherRate(baseline, history, now, expired, out status);

            default:
                status = $"未知的確認條件：{baseline.Post.Kind}";
                return PreviousActionResult.Failed;
        }
    }

    private static PreviousActionResult EvaluateSpend(
        ActionOutcomeBaseline baseline, GameState? current, bool expired, out string status)
    {
        var resource = baseline.Post.Resource ?? TrackedResource.Food;
        var before = GameActionRegistry.Select(baseline.State, resource);
        var after = GameActionRegistry.Select(current, resource);
        var label = GameActionRegistry.Label(resource);

        if (before?.IsUsable != true)
        { status = $"基準線的{label}讀值不可靠"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }
        if (after?.IsUsable != true)
        { status = $"目前的{label}讀值不可靠，繼續等待"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }

        var dropped = before.Value.GetValueOrDefault() - after.Value.GetValueOrDefault();
        var required = baseline.Post.Amount * SpendTolerance;
        if (dropped >= required)
        { status = $"{label}已扣除 {dropped}（門檻 {required:0}）"; return PreviousActionResult.Confirmed; }

        status = $"{label}尚未出現預期扣除：已變化 {dropped}，需要 {required:0}";
        return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain;
    }

    private static PreviousActionResult EvaluateIncrease(
        Observations.ObservedValue<int>? before, Observations.ObservedValue<int>? after,
        bool expired, string label, out string status)
    {
        if (before?.IsUsable != true || after?.IsUsable != true)
        { status = $"{label}讀值不可靠，繼續等待"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }
        if (after.Value.GetValueOrDefault() > before.Value.GetValueOrDefault())
        { status = $"{label}已自 {before.Value.GetValueOrDefault()} 增加到 {after.Value.GetValueOrDefault()}"; return PreviousActionResult.Confirmed; }
        status = $"{label}尚未增加";
        return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain;
    }

    private static PreviousActionResult EvaluateAge(
        ActionOutcomeBaseline baseline, GameState? current, bool expired, out string status)
    {
        var before = baseline.State.Age;
        var after = current?.Age;
        if (before is null || after is null)
        { status = "時代讀值不可靠，繼續等待"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }
        if (after.Value > before.Value)
        { status = $"時代已自 {before.Value} 推進到 {after.Value}"; return PreviousActionResult.Confirmed; }
        status = "時代尚未改變";
        return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain;
    }

    private static PreviousActionResult EvaluateGatherRate(
        ActionOutcomeBaseline baseline, GameHistory? history, DateTimeOffset now, bool expired, out string status)
    {
        var resource = baseline.Post.Resource ?? TrackedResource.Food;
        var label = GameActionRegistry.Label(resource);
        if (history is null || baseline.GatherRate is null)
        { status = $"缺少{label}採集速率基準線"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }

        var rate = CurrentGatherRate(history, resource, now);
        if (rate is null)
        { status = $"{label}採集速率樣本不足，繼續等待"; return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain; }
        if (rate.Value > baseline.GatherRate.Value)
        { status = $"{label}採集速率已自 {baseline.GatherRate.Value:0.0} 上升到 {rate.Value:0.0}／分"; return PreviousActionResult.Confirmed; }

        status = $"{label}採集速率尚未上升";
        return expired ? PreviousActionResult.Failed : PreviousActionResult.Uncertain;
    }

    /// <summary>重用 <see cref="GameHistorySummarizer"/> 既有的趨勢計算，不另外實作。</summary>
    public static double? CurrentGatherRate(GameHistory history, TrackedResource resource, DateTimeOffset now)
    {
        var summary = GameHistorySummarizer.Summarize(history, TimeSpan.FromSeconds(60), now);
        var trend = resource switch
        {
            TrackedResource.Food => summary.Food,
            TrackedResource.Wood => summary.Wood,
            TrackedResource.Gold => summary.Gold,
            TrackedResource.Stone => summary.Stone,
            _ => null,
        };
        return trend?.ChangePerMinute;
    }
}
