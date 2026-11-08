namespace WowStopwatcher.Core;

/// <summary>Builds the Today timeline layout from today's sessions.</summary>
public static class TodayTimelineBuilder
{
    /// <summary>Maximum share of the track the grey remaining section may take.</summary>
    public const double MaxRemainingShare = 0.35;

    /// <summary>Pins closer than this (fraction of the track) on the same level are moved up a level.</summary>
    public const double PinSpacing = 0.026;

    public static TodayTimeline Build(StopwatchEngineSnapshot snapshot) =>
        Build(snapshot.AllStopwatches, snapshot.TodaySessions, snapshot.NowUtc);

    /// <param name="stopwatches">All definitions including deleted (needed for names and colors).</param>
    /// <param name="todaySessions">Today's sessions, including the running one (StopUtc == null).</param>
    /// <param name="nowUtc">Used to measure the running session.</param>
    public static TodayTimeline Build(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        IReadOnlyList<Session> todaySessions,
        DateTimeOffset nowUtc)
    {
        var byId = stopwatches.ToDictionary(s => s.Id);
        var pieces = todaySessions
            .Where(s => byId.ContainsKey(s.StopwatchId))
            .OrderBy(s => s.StartUtc)
            .ThenBy(s => s.Id)
            .Select(s => (Session: s, Duration: s.GetDuration(nowUtc)))
            .ToList();

        // The bar shows the union of all sessions; overlapping stopwatches share a slice (one lane each).
        var slices = SessionSlicer.Build(todaySessions, stopwatches, nowUtc);
        var logged = SessionSlicer.Total(slices);

        // Each stopwatch's own total (for goals) is its full time, overlaps included.
        var totals = pieces
            .GroupBy(p => p.Session.StopwatchId)
            .ToDictionary(g => g.Key, g => Sum(g.Select(p => p.Duration)));

        var remainingGoals = stopwatches
            .Where(s => !s.IsDeleted && s.HasGoal)
            .Select(s => new RemainingGoal(
                s.Id, s.Name, s.Color,
                Max(TimeSpan.Zero, s.Goal!.Value - totals.GetValueOrDefault(s.Id))))
            .Where(g => g.Remaining > TimeSpan.Zero)
            .OrderByDescending(g => g.Remaining)
            .ToList();
        var remaining = Sum(remainingGoals.Select(g => g.Remaining));

        double remainingShare;
        var capped = false;
        if (remaining <= TimeSpan.Zero)
        {
            remainingShare = 0;
        }
        else if (logged <= TimeSpan.Zero)
        {
            remainingShare = 1;
        }
        else
        {
            remainingShare = remaining / (logged + remaining);
            if (remainingShare > MaxRemainingShare)
            {
                remainingShare = MaxRemainingShare;
                capped = true;
            }
        }

        var loggedShare = logged > TimeSpan.Zero || remainingShare > 0 ? 1 - remainingShare : 0;
        var segments = new List<TimelineSegment>();
        var starts = new List<double>();
        var widths = new List<double>();
        var cursor = 0.0;
        foreach (var slice in slices)
        {
            var width = logged > TimeSpan.Zero ? loggedShare * (slice.Duration / logged) : 0;
            var live = slice.IsLive;
            var title = slice.Lanes.Count == 1
                ? $"{byId[slice.Lanes[0].StopwatchId].Name} · {(live ? "running" : "session")}"
                : $"{string.Join(" + ", slice.Lanes.Select(l => byId[l.StopwatchId].Name))} · {(live ? "running together" : "together")}";
            segments.Add(new TimelineSegment(
                TimelineSegmentKind.Logged, slice.Lanes[0].StopwatchId, slice.Lanes[0].Color, slice.Duration, live,
                title, null, cursor, width, false, slice.Lanes));
            starts.Add(cursor);
            widths.Add(width);
            cursor += width;
        }

        if (remainingShare > 0)
        {
            // One segment per unfinished goal, nearest goal first; the share is split in proportion to what is left.
            var ordered = remainingGoals
                .OrderBy(g => g.Remaining)
                .ThenBy(g => byId[g.StopwatchId].SortOrder)
                .ThenBy(g => g.StopwatchId, StringComparer.Ordinal)
                .ToList();
            var start = loggedShare;
            for (var i = 0; i < ordered.Count; i++)
            {
                var goal = ordered[i];
                var width = remainingShare * (goal.Remaining / remaining);
                segments.Add(new TimelineSegment(
                    TimelineSegmentKind.Remaining, goal.StopwatchId, goal.Color, goal.Remaining, false,
                    $"{goal.Name} · left to goal", $"Goal {TimeFormat.GoalLabel(byId[goal.StopwatchId].Goal!.Value)}",
                    start, width, capped && i == 0));
                start += width;
            }
        }

        var pins = BuildPins(stopwatches, pieces, slices, starts, widths);
        return new TodayTimeline(
            segments, loggedShare, pins.Pins, pins.MaxLevel, logged, remaining, remainingGoals, capped);
    }

    private static (IReadOnlyList<TimelineGoalPin> Pins, int MaxLevel) BuildPins(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        List<(Session Session, TimeSpan Duration)> pieces,
        IReadOnlyList<SessionSlice> slices,
        List<double> starts,
        List<double> widths)
    {
        var raw = new List<(StopwatchDefinition Def, double Position, string Sub)>();
        foreach (var def in stopwatches.Where(s => !s.IsDeleted && s.HasGoal))
        {
            var goal = def.Goal!.Value;
            var cumulative = TimeSpan.Zero;
            var loggedBefore = TimeSpan.Zero;
            for (var i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                if (slice.Lanes.All(l => l.StopwatchId != def.Id))
                {
                    loggedBefore += slice.Duration;
                    continue;
                }

                if (cumulative + slice.Duration >= goal)
                {
                    var offset = goal - cumulative;
                    var position = starts[i] + widths[i] * (offset / slice.Duration);
                    var into = loggedBefore + offset;

                    // The goal was reached during the session of this stopwatch that covers that instant.
                    var instant = slice.StartUtc + offset;
                    var own = pieces.Where(p => p.Session.StopwatchId == def.Id && p.Duration > TimeSpan.Zero).Select(p => p.Session).ToList();
                    var index = own.FindIndex(s => s.StartUtc <= instant && instant <= s.StartUtc + s.GetDuration(slice.EndUtc));
                    var session = own[index];
                    var live = session.IsOpen ? " (running now)" : "";
                    var sub = $"During its {TimeFormat.Ordinal(index + 1)} session{live} · {TimeFormat.HoursMinutes(into)} into today's log";
                    raw.Add((def, position, sub));
                    break;
                }

                cumulative += slice.Duration;
                loggedBefore += slice.Duration;
            }
        }

        var lastOnLevel = new List<double>();
        var pins = new List<TimelineGoalPin>();
        foreach (var (def, position, sub) in raw.OrderBy(r => r.Position))
        {
            var level = 0;
            while (level < lastOnLevel.Count && position - lastOnLevel[level] < PinSpacing)
            {
                level++;
            }

            if (level == lastOnLevel.Count)
            {
                lastOnLevel.Add(position);
            }
            else
            {
                lastOnLevel[level] = position;
            }

            pins.Add(new TimelineGoalPin(
                def.Id, def.Color, $"{def.Name} · goal {TimeFormat.GoalLabel(def.Goal!.Value)} reached", sub, position, level));
        }

        return (pins, pins.Count == 0 ? 0 : pins.Max(p => p.Level));
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> values) =>
        values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
