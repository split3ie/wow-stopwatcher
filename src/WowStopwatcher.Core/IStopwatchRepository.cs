namespace WowStopwatcher.Core;

/// <summary>Persistent storage of stopwatch definitions.</summary>
public interface IStopwatchRepository
{
    /// <summary>All stopwatches ordered by SortOrder (then name); deleted ones only when <paramref name="includeDeleted"/>.</summary>
    IReadOnlyList<StopwatchDefinition> GetAll(bool includeDeleted = false);

    /// <summary>The stopwatch with <paramref name="id"/> (deleted or not), or null.</summary>
    StopwatchDefinition? Get(string id);

    /// <summary>Inserts a new stopwatch. Throws if the id already exists.</summary>
    void Add(StopwatchDefinition stopwatch);

    /// <summary>Overwrites all fields of an existing stopwatch (matched by Id).</summary>
    void Update(StopwatchDefinition stopwatch);

    /// <summary>All recorded goal changes, ordered by time.</summary>
    IReadOnlyList<GoalChange> GetGoalChanges();

    /// <summary>All recorded goal resets, ordered by time.</summary>
    IReadOnlyList<GoalReset> GetGoalResets();

    /// <summary>Records a goal reset.</summary>
    void AddGoalReset(GoalReset reset);

    /// <summary>Marks the stopwatch as deleted; its sessions are kept.</summary>
    void SoftDelete(string id);

    /// <summary>Sets SortOrder = index in <paramref name="orderedIds"/> for each id, in one transaction.</summary>
    void SaveSortOrder(IReadOnlyList<string> orderedIds);
}
