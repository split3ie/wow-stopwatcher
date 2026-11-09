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
    public static StatisticsResult Build(
        IEnumerable<Session> sessions,
        IReadOnlyList<StopwatchDefinition> stopwatches,
        DateTimeOffset nowUtc,
        TimeZoneInfo zone)
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

        var days = sessionList
            .Where(s => byId.ContainsKey(s.StopwatchId))
            .GroupBy(s => LocalDays.ToLocalDate(s.StartUtc, zone))
            .OrderByDescending(g => g.Key)
            .Select(g => (Date: g.Key, Sessions: g.OrderBy(s => s.StartUtc).ThenBy(s => s.Id).ToList()))
            .ToList();

        // The day total (and so the scale) is the union of all sessions: overlapping time counts once.
        var maxMinutes = days.Count == 0
            ? 0
            : days.Max(d => SessionSlicer.Total(SessionSlicer.Build(d.Sessions, stopwatches, nowUtc)).TotalMinutes);
        var scale = Math.Max(MinScaleMinutes, (int)Math.Ceiling(maxMinutes / ScaleStepMinutes) * ScaleStepMinutes);

        var result = days.Select(d => BuildDay(d.Date, d.Sessions, stopwatches, existsFrom, nowUtc, scale)).ToList();
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
        DateTimeOffset nowUtc,
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

        bool GoalListed(StopwatchDefinition s) =>
            !s.IsDeleted && s.HasGoal && (!existsFrom.TryGetValue(s.Id, out var from) || from <= date);

        // A deleted goal stopwatch counts like a normal one on days where it has time.
        bool GoalCounted(StopwatchDefinition s) =>
            s.HasGoal && (s.IsDeleted ? perStopwatch.ContainsKey(s.Id) : GoalListed(s));

        var totals = stopwatches
            .Where(s => perStopwatch.ContainsKey(s.Id) || GoalListed(s))
            .OrderBy(s => s.SortOrder)
            .Select(s =>
            {
                var total = perStopwatch.GetValueOrDefault(s.Id);
                var reached = s.HasGoal && total >= s.Goal!.Value;
                var share = dayTotal > TimeSpan.Zero ? Math.Clamp(total / dayTotal, 0, 1) : 0;
                var overtime = reached && dayTotal > TimeSpan.Zero
                    ? Math.Clamp((total - s.Goal!.Value) / dayTotal, 0, share)
                    : 0;
                return new DayStopwatchTotal(s.Id, s.Name, s.Color, s.IsDeleted, total, share, s.Goal, reached, s.IsIndependent, overtime);
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
