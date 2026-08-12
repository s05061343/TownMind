namespace AgePilot.Core.Automation;

public enum ActionExecutionPhase
{
    Ready,
    AwaitingConfirmation,
    Confirmed,
    Failed,
}

public sealed record ActionPrecondition(string Name, bool IsSatisfied, string FailureReason);

public sealed record ActionExecutionState(
    string ActionId,
    ActionExecutionPhase Phase,
    DateTimeOffset StartedAt,
    DateTimeOffset Deadline,
    string Status)
{
    public static ActionExecutionState Start(
        string actionId,
        DateTimeOffset now,
        TimeSpan timeout,
        IReadOnlyList<ActionPrecondition> preconditions)
    {
        var failed = preconditions.FirstOrDefault(item => !item.IsSatisfied);
        return failed is not null
            ? new(actionId, ActionExecutionPhase.Failed, now, now, failed.FailureReason)
            : new(actionId, ActionExecutionPhase.Ready, now, now.Add(timeout), "前置條件已確認");
    }

    public ActionExecutionState MarkSent() => Phase == ActionExecutionPhase.Ready
        ? this with { Phase = ActionExecutionPhase.AwaitingConfirmation, Status = "輸入已送出，等待遊戲結果" }
        : this;

    public ActionExecutionState Observe(bool confirmed, DateTimeOffset now) => Phase switch
    {
        ActionExecutionPhase.AwaitingConfirmation when confirmed =>
            this with { Phase = ActionExecutionPhase.Confirmed, Status = "遊戲結果已確認" },
        ActionExecutionPhase.AwaitingConfirmation when now >= Deadline =>
            this with { Phase = ActionExecutionPhase.Failed, Status = "逾時且未取得成功證據" },
        _ => this,
    };
}
