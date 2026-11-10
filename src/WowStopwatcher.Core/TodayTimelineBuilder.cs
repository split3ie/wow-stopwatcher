using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core;

/// <summary>Builds the Today timeline layout from today's sessions.</summary>
public static class TodayTimelineBuilder
{
    /// <summary>Maximum share of the track the grey remaining section may take.</summary>
    public const double MaxRemainingShare = 0.35;

    /// <summary>Pins closer than this (fraction of the track) on the same level are moved up a level.</summary>
    public const double PinSpacing = 0.026;

    public static TodayTimeline Build(StopwatchEngineSnapshot snapshot) =>
        Build(snapshot.AllStopwatches, snapshot.TodaySessions, snapshot.NowUtc, snapshot);

    /// <param name="stopwatches">All definitions including deleted (needed for names and colors).</param>
    /// <param name="todaySessions">Today's sessions, including the running one (StopUtc == null).</param>
    /// <param name="nowUtc">Used to measure the running session.</param>
    public static TodayTimeline Build(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        IReadOnlyList<Session> todaySessions,
        DateTimeOffset nowUtc) =>
        Build(stopwatches, todaySessions, nowUtc, null);

    private static TodayTimeline Build(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        IReadOnlyList<Session> todaySessions,
        DateTimeOffset nowUtc,
        StopwatchEngineSnapshot? snapshot)
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
                snapshot is not null && s.GoalPerCycle && snapshot.GetGoalStatus(s.Id) is { } status
                    ? status.Remaining
                    : Max(TimeSpan.Zero, s.Goal!.Value - totals.GetValueOrDefault(s.Id))))
            .Where(g => g.Remaining > TimeSpan.Zero)
            .OrderByDescending(g => g.Remaining)
            .ToList();
        var goalsTotal = Sum(remainingGoals.Select(g => g.Remaining));

        // Independent stopwatches run alongside the regular ones, so their remaining time fits inside the regular
        // goals' time: wall-clock remaining is the regular sum, or the longest independent goal if that is longer.
        var independentIds = stopwatches.Where(s => s.IsIndependent).Select(s => s.Id).ToHashSet();
        var regularRemaining = Sum(remainingGoals.Where(g => !independentIds.Contains(g.StopwatchId)).Select(g => g.Remaining));
        var independentRemaining = remainingGoals
            .Where(g => independentIds.Contains(g.StopwatchId))
            .Select(g => g.Remaining)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        var remaining = Max(regularRemaining, independentRemaining);

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
                ? Loc.T(live ? "timeline.running" : "timeline.session", ("name", byId[slice.Lanes[0].StopwatchId].Name))
                : Loc.T(live ? "timeline.runningTogether" : "timeline.together",
                    ("names", string.Join(" + ", slice.Lanes.Select(l => byId[l.StopwatchId].Name))));
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
                var width = remainingShare * (goal.Remaining / goalsTotal);
                segments.Add(new TimelineSegment(
                    TimelineSegmentKind.Remaining, goal.StopwatchId, goal.Color, goal.Remaining, false,
                    Loc.T("timeline.leftToGoal", ("name", goal.Name)),
                    Loc.T("timeline.goalLabel", ("goal", TimeFormat.GoalLabel(byId[goal.StopwatchId].Goal!.Value))),
                    start, width, capped && i == 0));
                start += width;
            }
        }

        var pins = BuildPins(stopwatches, pieces, slices, starts, widths, snapshot);
        return new TodayTimeline(
            segments, loggedShare, pins.Pins, pins.MaxLevel, logged, remaining, remainingGoals, capped);
    }

    private static (IReadOnlyList<TimelineGoalPin> Pins, int MaxLevel) BuildPins(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        List<(Session Session, TimeSpan Duration)> pieces,
        IReadOnlyList<SessionSlice> slices,
        List<double> starts,
        List<double> widths,
        StopwatchEngineSnapshot? snapshot)
    {
        var raw = new List<(StopwatchDefinition Def, double Position, string Title, string Sub)>();
        foreach (var def in stopwatches.Where(s => !s.IsDeleted && s.HasGoal))
        {
            if (snapshot is not null && def.GoalPerCycle && snapshot.GetGoalCycles(def.Id).Count > 0)
            {
                AddCyclePins(raw, def, snapshot, pieces, slices, starts, widths);
                continue;
            }

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
                    var sub = Loc.T(
                        session.IsOpen ? "timeline.pin.duringSessionRunning" : "timeline.pin.duringSession",
                        ("ordinal", TimeFormat.Ordinal(index + 1)), ("number", index + 1), ("time", TimeFormat.HoursMinutes(into)));
                    raw.Add((def, position, Loc.T("timeline.goalReached", ("name", def.Name), ("goal", TimeFormat.GoalLabel(def.Goal!.Value))), sub));
                    break;
                }

                cumulative += slice.Duration;
                loggedBefore += slice.Duration;
            }
        }

        var lastOnLevel = new List<double>();
        var pins = new List<TimelineGoalPin>();
        foreach (var (def, position, title, sub) in raw.OrderBy(r => r.Position))
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
                def.Id, def.Color, title, sub, position, level));
        }

        return (pins, pins.Count == 0 ? 0 : pins.Max(p => p.Level));
    }

    // One pin per cycle of a per-cycle stopwatch that was reached today, placed at the instant it was reached.
    private static void AddCyclePins(
        List<(StopwatchDefinition Def, double Position, string Title, string Sub)> raw,
        StopwatchDefinition def,
        StopwatchEngineSnapshot snapshot,
        List<(Session Session, TimeSpan Duration)> pieces,
        IReadOnlyList<SessionSlice> slices,
        List<double> starts,
        List<double> widths)
    {
        var zone = snapshot.TimeZone;
        var (dayStart, dayEnd) = LocalDays.DayRangeUtc(snapshot.Today, zone);
        var own = pieces.Where(p => p.Session.StopwatchId == def.Id && p.Duration > TimeSpan.Zero).Select(p => p.Session).ToList();
        foreach (var cycle in snapshot.GetGoalCycles(def.Id))
        {
            if (cycle.ReachedUtc is not { } instant || instant < dayStart || instant >= dayEnd)
            {
                continue;
            }

            var loggedBefore = TimeSpan.Zero;
            for (var i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                if (slice.Lanes.Any(l => l.StopwatchId == def.Id) && slice.StartUtc <= instant && instant <= slice.EndUtc)
                {
                    var offset = instant - slice.StartUtc;
                    var position = starts[i] + widths[i] * (offset / slice.Duration);
                    var into = TimeFormat.HoursMinutes(loggedBefore + offset);
                    string sub;
                    if (cycle.Date == snapshot.Today)
                    {
                        var index = own.FindIndex(s => s.StartUtc <= instant && instant <= s.StartUtc + s.GetDuration(slice.EndUtc));
                        sub = index >= 0
                            ? Loc.T(
                                own[index].IsOpen ? "timeline.pin.duringSessionRunning" : "timeline.pin.duringSession",
                                ("ordinal", TimeFormat.Ordinal(index + 1)), ("number", index + 1), ("time", into))
                            : Loc.T("timeline.pin.intoLog", ("time", into));
                    }
                    else
                    {
                        var started = TimeZoneInfo.ConvertTime(cycle.StartUtc, zone);
                        var day = cycle.Date.ToDateTime(TimeOnly.MinValue).ToString(Loc.T("format.weekdayShort"), Loc.Culture);
                        sub = Loc.T(
                            "timeline.pin.earlierCycle",
                            ("day", day), ("started", started.ToString(Loc.T("format.weekdayTime"), Loc.Culture)), ("time", into));
                    }

                    raw.Add((def, position, Loc.T("timeline.goalReached", ("name", def.Name), ("goal", TimeFormat.GoalLabel(cycle.Goal))), sub));
                    break;
                }

                loggedBefore += slice.Duration;
            }
        }
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> values) =>
        values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
