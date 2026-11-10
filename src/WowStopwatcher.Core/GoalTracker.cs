namespace WowStopwatcher.Core;

/// <summary>
/// Detects when a stopwatch's goal becomes reached. Call <see cref="Check"/> after each engine update.
/// Used from one thread only; not thread-safe.
/// </summary>
public sealed class GoalTracker
{
    private readonly HashSet<string> _reached = [];
    private HashSet<(string StopwatchId, DateTimeOffset StartUtc)> _reachedCycles = [];
    private HashSet<string> _perCycleIds = [];
    private DateOnly? _day;

    /// <summary>
    /// Returns the stopwatches whose goal became reached since the previous call, in snapshot order.
    /// The first call only records a baseline and returns an empty list. An each-day goal starts from zero on a new day;
    /// a per-cycle goal is reported once per newly reached cycle (also across midnight), and a stopwatch at most once per call.
    /// A stopwatch that just became per-cycle (or was not seen before) only records its reached cycles as a baseline.
    /// </summary>
    public IReadOnlyList<StopwatchDefinition> Check(StopwatchEngineSnapshot snapshot)
    {
        var isBaseline = _day is null;
        if (_day != snapshot.Today)
        {
            _reached.Clear();
            _day = snapshot.Today;
        }

        var currentCycles = new HashSet<(string, DateTimeOffset)>();
        var currentPerCycle = new HashSet<string>();
        var newlyReached = new List<StopwatchDefinition>();
        foreach (var definition in snapshot.Stopwatches)
        {
            var wasPerCycle = _perCycleIds.Contains(definition.Id);
            if (definition.HasGoal && definition.GoalPerCycle)
            {
                currentPerCycle.Add(definition.Id);
            }

            if (definition.HasGoal && definition.GoalPerCycle && snapshot.GetGoalCycles(definition.Id) is { Count: > 0 } cycles)
            {
                var isNew = false;
                foreach (var cycle in cycles.Where(c => c.IsReached))
                {
                    var key = (definition.Id, cycle.StartUtc);
                    currentCycles.Add(key);
                    isNew |= !_reachedCycles.Contains(key);
                }

                if (isNew && !isBaseline && wasPerCycle)
                {
                    newlyReached.Add(definition);
                }

                continue;
            }

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

        _reachedCycles = currentCycles;
        _perCycleIds = currentPerCycle;
        return newlyReached;
    }
}
