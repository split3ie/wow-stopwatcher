namespace WowStopwatcher.Core;

/// <summary>
/// Detects when a stopwatch's daily goal becomes reached. Call <see cref="Check"/> after each engine update.
/// Used from one thread only; not thread-safe.
/// </summary>
public sealed class GoalTracker
{
    private readonly HashSet<string> _reached = [];
    private DateOnly? _day;

    /// <summary>
    /// Returns the stopwatches whose goal became reached since the previous call, in snapshot order.
    /// The first call only records a baseline and returns an empty list. A new day starts from zero.
    /// </summary>
    public IReadOnlyList<StopwatchDefinition> Check(StopwatchEngineSnapshot snapshot)
    {
        var isBaseline = _day is null;
        if (_day != snapshot.Today)
        {
            _reached.Clear();
            _day = snapshot.Today;
        }

        var newlyReached = new List<StopwatchDefinition>();
        foreach (var definition in snapshot.Stopwatches)
        {
            var reached = definition.HasGoal && snapshot.GetTodayTotal(definition.Id) >= definition.Goal!.Value;
            if (!reached)
            {
                _reached.Remove(definition.Id);
            }
            else if (_reached.Add(definition.Id) && !isBaseline)
            {
                newlyReached.Add(definition);
            }
        }

        return newlyReached;
    }
}
