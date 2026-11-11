namespace WowStopwatcher.Core;

/// <summary>Builds per-day statistics from sessions.</summary>
public static class DailyStatisticsBuilder
{
    public const int MinScaleMinutes = 540;
    public const int ScaleStepMinutes = 180;

    /// <param name="sessions">All sessions (the earliest one of a stopwatch tells when it first existed);
    /// days are the local date of the session start.</param>
    /// <param name="stopwatches">All definitions including deleted ones.</param>
    /// <param name="nowUtc">Measures any open session.</param>
    /// <param name="zone">The local time zone (use <c>TimeProvider.LocalTimeZone</c>).</param>
    /// <remarks>Day totals and segment widths are wall-clock based. The shared scale fits the longest day in either
    /// wall-clock time or combined-bar length (overlap time counted once per stopwatch), so combined bars never overflow.</remarks>
    /// <param name="goalChanges">Recorded goal changes: a day uses the goal in effect at its end.
    /// Days without history use the current goal.</param>
    /// <param name="goalResets">Recorded goal resets of per-cycle goals; each cancels the open cycle.</param>
    public static StatisticsResult Build(
        IEnumerable<Session> sessions,
        IReadOnlyList<StopwatchDefinition> stopwatches,
        DateTimeOffset nowUtc,
        TimeZoneInfo zone,
        IReadOnlyList<GoalChange>? goalChanges = null,
        IReadOnlyList<GoalReset>? goalResets = null)
    {
        var byId = stopwatches.ToDictionary(s => s.Id);
        var sessionList = sessions.ToList();

        // A stopwatch exists from the earlier of its first session and ActiveSinceUtc; unknown means every day.
        var existsFrom = new Dictionary<string, DateOnly>();
        foreach (var s in stopwatches)
        {
            var dates = sessionList.Where(x => x.StopwatchId == s.Id).Select(x => LocalDays.ToLocalDate(x.StartUtc, zone)).ToList();
            if (s.ActiveSinceUtc is { } active)
                dates.Add(LocalDays.ToLocalDate(active, zone));
            if (dates.Count > 0)
                existsFrom[s.Id] = dates.Min();
        }

        var changesById = (goalChanges ?? [])
            .GroupBy(c => c.StopwatchId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.ChangedUtc).Select(c => (Date: LocalDays.ToLocalDate(c.ChangedUtc, zone), c.PreviousGoal)).ToList());

        // Per-cycle goals: cycles are built once per stopwatch and grouped by the date they start on.
        var cyclesById = new Dictionary<string, (DateOnly AnchorDate, Dictionary<DateOnly, List<GoalCycle>> ByDate)>();
        foreach (var s in stopwatches)
        {
            if (s.GoalPerCycle && s.GoalSinceUtc is { } anchor && s.Goal is { } goal && goal > TimeSpan.Zero)
            {
                var byDate = GoalCycleBuilder.Build(s.Id, anchor, s.Goal, sessionList, goalChanges ?? [], goalResets ?? [], nowUtc, zone)
                    .GroupBy(c => c.Date)
                    .ToDictionary(g => g.Key, g => g.ToList());
                cyclesById[s.Id] = (LocalDays.ToLocalDate(anchor, zone), byDate);
            }
        }

        var days = sessionList
            .Where(s => byId.ContainsKey(s.StopwatchId))
            .GroupBy(s => LocalDays.ToLocalDate(s.StartUtc, zone))
            .OrderByDescending(g => g.Key)
            .Select(g => (Date: g.Key, Sessions: g.OrderBy(s => s.StartUtc).ThenBy(s => s.Id).ToList()))
            .ToList();

        // The day total is the union of all sessions: overlapping time counts once. The scale must also fit the
        // combined bar, where overlap time is drawn once per running stopwatch (duration x lane count).
        var maxMinutes = days.Count == 0
            ? 0
            : days.Max(d =>
            {
                var slices = SessionSlicer.Build(d.Sessions, stopwatches, nowUtc);
                var wallClock = SessionSlicer.Total(slices).TotalMinutes;
                var combined = slices.Sum(s => s.Duration.TotalMinutes * s.Lanes.Count);
                return Math.Max(wallClock, combined);
            });
        var scale = Math.Max(MinScaleMinutes, (int)Math.Ceiling(maxMinutes / ScaleStepMinutes) * ScaleStepMinutes);

        var result = days.Select(d => BuildDay(d.Date, d.Sessions, stopwatches, existsFrom, changesById, cyclesById, nowUtc, zone, scale)).ToList();
        var ticks = Enumerable.Range(0, 4)
            .Select(i => new StatisticsTick(i / 3.0, $"{scale * i / 3 / 60}h"))
            .ToList();
        return new StatisticsResult(result, scale, ticks);
    }

    private static DayStatistics BuildDay(
        DateOnly date,
        List<Session> sessions,
        IReadOnlyList<StopwatchDefinition> stopwatches,
        Dictionary<string, DateOnly> existsFrom,
        Dictionary<string, List<(DateOnly Date, TimeSpan? PreviousGoal)>> changesById,
        Dictionary<string, (DateOnly AnchorDate, Dictionary<DateOnly, List<GoalCycle>> ByDate)> cyclesById,
        DateTimeOffset nowUtc,
        TimeZoneInfo zone,
        int scaleMinutes)
    {
        var scale = TimeSpan.FromMinutes(scaleMinutes);
        var segments = SessionSlicer.Build(sessions, stopwatches, nowUtc)
            .Select(s => new DaySegment(s.Lanes[0].StopwatchId, s.Lanes[0].Color, s.Duration, s.Duration / scale, s.Lanes))
            .ToList();

        var perStopwatch = sessions
            .GroupBy(s => s.StopwatchId)
            .ToDictionary(g => g.Key, g => g.Aggregate(TimeSpan.Zero, (a, s) => a + s.GetDuration(nowUtc)));
        var dayTotal = segments.Aggregate(TimeSpan.Zero, (a, s) => a + s.Duration);

        // The goal at the end of this day: the previous goal of the first change after it, else the current goal.
        TimeSpan? GoalOf(StopwatchDefinition s)
        {
            var goal = s.Goal;
            if (changesById.TryGetValue(s.Id, out var changes))
            {
                foreach (var change in changes)
                {
                    if (change.Date > date)
                    {
                        goal = change.PreviousGoal;
                        break;
                    }
                }
            }

            return goal is { } g && g > TimeSpan.Zero ? g : null;
        }

        // The cycles starting on this day, or null when the stopwatch is judged per day here.
        List<GoalCycle>? CyclesOf(StopwatchDefinition s)
        {
            if (!cyclesById.TryGetValue(s.Id, out var entry) || date < entry.AnchorDate)
                return null;
            return entry.ByDate.TryGetValue(date, out var list) ? list : [];
        }

        bool GoalListed(StopwatchDefinition s) =>
            CyclesOf(s) is { } cycles
                ? cycles.Count > 0 && !s.IsDeleted
                : !s.IsDeleted && GoalOf(s) is not null && (!existsFrom.TryGetValue(s.Id, out var from) || from <= date);

        // A deleted goal stopwatch counts like a normal one on days where it has time.
        bool GoalCounted(StopwatchDefinition s) =>
            CyclesOf(s) is { } cycles
                ? cycles.Count > 0 && (!s.IsDeleted || perStopwatch.ContainsKey(s.Id))
                : GoalOf(s) is not null && (s.IsDeleted ? perStopwatch.ContainsKey(s.Id) : GoalListed(s));

        DayStopwatchTotal CycleTotal(StopwatchDefinition s, List<GoalCycle> cycles, TimeSpan total, double share)
        {
            if (cycles.Count == 0)
                return new DayStopwatchTotal(s.Id, s.Name, s.Color, s.IsDeleted, total, share, null, false, s.IsIndependent, 0);

            var reachedCycles = cycles.Where(c => c.IsReached).ToList();
            var reachedUtc = reachedCycles.Count > 0 ? reachedCycles[0].ReachedUtc : null;
            var open = cycles.FirstOrDefault(c => !c.IsReached && !c.IsCancelled);
            var cancelled = cycles.FirstOrDefault(c => c.IsCancelled);

            var overtime = 0.0;
            if (reachedUtc is { } at && dayTotal > TimeSpan.Zero && LocalDays.ToLocalDate(at, zone) == date)
            {
                var after = TimeSpan.Zero;
                foreach (var session in sessions.Where(x => x.StopwatchId == s.Id))
                {
                    var start = session.StartUtc > at ? session.StartUtc : at;
                    var end = session.StopUtc ?? nowUtc;
                    if (end > start)
                        after += end - start;
                }

                overtime = Math.Clamp(after / dayTotal, 0, share);
            }

            return new DayStopwatchTotal(
                s.Id, s.Name, s.Color, s.IsDeleted, total, share, cycles[^1].Goal, reachedCycles.Count > 0, s.IsIndependent, overtime,
                reachedCycles.Count, reachedUtc, open?.Progress ?? cancelled?.Progress, cancelled is not null);
        }

        var totals = stopwatches
            .Where(s => perStopwatch.ContainsKey(s.Id) || GoalListed(s))
            .OrderBy(s => s.SortOrder)
            .Select(s =>
            {
                var total = perStopwatch.GetValueOrDefault(s.Id);
                var share = dayTotal > TimeSpan.Zero ? Math.Clamp(total / dayTotal, 0, 1) : 0;
                if (CyclesOf(s) is { } cycles)
                    return CycleTotal(s, cycles, total, share);

                var goal = GoalOf(s);
                var reached = goal is { } g && total >= g;
                var overtime = reached && dayTotal > TimeSpan.Zero
                    ? Math.Clamp((total - goal!.Value) / dayTotal, 0, share)
                    : 0;
                return new DayStopwatchTotal(s.Id, s.Name, s.Color, s.IsDeleted, total, share, goal, reached, s.IsIndependent, overtime,
                    reached ? 1 : 0);
            })
            .ToList();

        return new DayStatistics(
            date,
            segments,
            totals,
            totals.Count(t => t.GoalReached),
            stopwatches.Count(GoalCounted),
            sessions.Count,
            dayTotal);
    }
}
