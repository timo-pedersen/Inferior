namespace Inferior.Core.DataBus;

public enum DispatchMode
{
    All,
    LatestPerDrain,
}

public enum RetentionMode
{
    None,
    Latest,
    History,
}

public enum ReplayMode
{
    None,
    Latest,
    History,
}

/// <summary>Stable dispatch and bounded-retention contract for one bus topic.</summary>
public readonly record struct TopicPolicy(
    DispatchMode Dispatch,
    RetentionMode Retention,
    int HistoryCapacity = 0)
{
    public static TopicPolicy OrderedTransient { get; } =
        new(DispatchMode.All, RetentionMode.None);

    public static TopicPolicy LatestState { get; } =
        new(DispatchMode.LatestPerDrain, RetentionMode.Latest);

    public static TopicPolicy OrderedHistory(int capacity) =>
        new TopicPolicy(DispatchMode.All, RetentionMode.History, capacity).Validated();

    public static TopicPolicy CoalescedHistory(int capacity) =>
        new TopicPolicy(DispatchMode.LatestPerDrain, RetentionMode.History, capacity).Validated();

    internal TopicPolicy Validated()
    {
        Validate();
        return this;
    }

    internal void Validate()
    {
        if (Retention == RetentionMode.History && HistoryCapacity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(HistoryCapacity),
                "History retention requires a positive bounded capacity.");

        if (Retention != RetentionMode.History && HistoryCapacity != 0)
            throw new ArgumentException(
                "HistoryCapacity must be zero unless retention mode is History.",
                nameof(HistoryCapacity));
    }
}
