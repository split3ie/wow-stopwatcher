namespace WowStopwatcher.Core;

/// <summary>
/// Splits a stopwatch's counted time into back-to-back goal cycles. Time past a goal flows into the next cycle immediately.
/// </summary>
public static class GoalCycleBuilder
{
    /// <summary>
    /// Builds the cycles of <paramref name="stopwatchId"/> from <paramref name="anchorUtc"/> up to <paramref name="nowUtc"/>, in chronological order.
    /// All cycles but the last are reached; the last one is open (possibly with zero progress).
    /// Counted time is the union of the stopwatch's sessions clipped to [anchor, now]. The goal at an instant is the
    /// <see cref="GoalChange.PreviousGoal"/> of the first change after it, or <paramref name="currentGoal"/> when there is none;
    /// time under a missing or non-positive goal is not counted. When the goal is missing at <paramref name="nowUtc"/>, the open cycle's Goal is zero.
    /// Returns an empty list when <paramref name="currentGoal"/> is null or not positive, or when the anchor is after <paramref name="nowUtc"/>.
    /// </summary>
    public static IReadOnlyList<GoalCycle> Build(
        string stopwatchId,
        DateTimeOffset anchorUtc,
        TimeSpan? currentGoal,
        IEnumerable<Session> sessions,
        IReadOnlyList<GoalChange> goalChanges,
        DateTimeOffset nowUtc,
        TimeZoneInfo zone)
    {
        if (currentGoal is not { } current || current <= TimeSpan.Zero || anchorUtc > nowUtc)
        {
            return [];
        }

        var intervals = MergedIntervals(stopwatchId, anchorUtc, sessions, nowUtc);

        var changes = new List<GoalChange>();
        foreach (var change in goalChanges)
        {
            if (change.StopwatchId == stopwatchId)
            {
                changes.Add(change);
            }
        }

        changes.Sort((a, b) => a.ChangedUtc.CompareTo(b.ChangedUtc));

        var cycles = new List<GoalCycle>();
        var cycleStart = anchorUtc;
        var progress = TimeSpan.Zero;

        // idx: first change strictly after the cursor; the goal in effect is that change's PreviousGoal.
        var idx = 0;
        while (idx < changes.Count && changes[idx].ChangedUtc <= anchorUtc)
        {
            idx++;
        }

        TimeSpan? GoalNow() => idx < changes.Count ? changes[idx].PreviousGoal : current;

        void Reach(DateTimeOffset at, TimeSpan goal)
        {
            cycles.Add(new GoalCycle(stopwatchId, cycleStart, LocalDays.ToLocalDate(cycleStart, zone), goal, goal, at));
            cycleStart = at;
        }

        // Applies all changes at or before the given instant.
        void ApplyChanges(DateTimeOffset upTo)
        {
            while (idx < changes.Count && changes[idx].ChangedUtc <= upTo)
            {
                var at = changes[idx].ChangedUtc;
                idx++;
                if (GoalNow() is { } goal && goal > TimeSpan.Zero)
                {
                    while (progress >= goal)
                    {
                        Reach(at, goal);
                        progress -= goal;
                    }
                }
            }
        }

        foreach (var (start, end) in intervals)
        {
            var pos = start;
            while (pos < end)
            {
                ApplyChanges(pos);
                var limit = idx < changes.Count && changes[idx].ChangedUtc < end ? changes[idx].ChangedUtc : end;
                if (GoalNow() is { } goal && goal > TimeSpan.Zero)
                {
                    var need = goal - progress;
                    if (limit - pos >= need)
                    {
                        pos += need;
                        Reach(pos, goal);
                        progress = TimeSpan.Zero;
                        continue;
                    }

                    progress += limit - pos;
                }

                pos = limit;
            }
        }

        ApplyChanges(nowUtc);

        var openGoal = GoalNow() is { } g && g > TimeSpan.Zero ? g : TimeSpan.Zero;
        cycles.Add(new GoalCycle(stopwatchId, cycleStart, LocalDays.ToLocalDate(cycleStart, zone), openGoal, progress, null));
        return cycles;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergedIntervals(
        string stopwatchId, DateTimeOffset anchorUtc, IEnumerable<Session> sessions, DateTimeOffset nowUtc)
    {
        var raw = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var session in sessions)
        {
            if (session.StopwatchId != stopwatchId)
            {
                continue;
            }

            var start = session.StartUtc > anchorUtc ? session.StartUtc : anchorUtc;
            var end = session.StopUtc ?? nowUtc;
            if (end > nowUtc)
            {
                end = nowUtc;
            }

            if (end > start)
            {
                raw.Add((start, end));
            }
        }

        raw.Sort((a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var interval in raw)
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                if (interval.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, interval.End);
                }
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }
}
