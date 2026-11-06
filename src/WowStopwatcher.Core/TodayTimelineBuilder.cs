namespace WowStopwatcher.Core;

/// <summary>Builds the Today timeline layout from today's sessions.</summary>
public static class TodayTimelineBuilder
{
    /// <summary>Maximum share of the track the grey remaining section may take.</summary>
    public const double MaxRemainingShare = 0.35;

    /// <summary>Pins closer than this (fraction of the track) on the same level are moved up a level.</summary>
    public const double PinSpacing = 0.026;

    private const string RemainingColor = "#C9CED6";

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

        var logged = Sum(pieces.Select(p => p.Duration));
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
        foreach (var (session, duration) in pieces)
        {
            var def = byId[session.StopwatchId];
            var width = logged > TimeSpan.Zero ? loggedShare * (duration / logged) : 0;
            var live = session.IsOpen;
            segments.Add(new TimelineSegment(
                TimelineSegmentKind.Logged, def.Id, def.Color, duration, live,
                $"{def.Name} · {(live ? "running" : "session")}", null, cursor, width, false));
            starts.Add(cursor);
            widths.Add(width);
            cursor += width;
        }

        if (remainingShare > 0)
        {
            var count = remainingGoals.Count;
            var title = $"Left to {count} goal{(count == 1 ? "" : "s")}" + (capped ? " · shortened to fit" : "");
            var parts = remainingGoals.Take(3).Select(g => $"{g.Name} {RemainingText(g.Remaining)}").ToList();
            var sub = string.Join(" · ", parts) + (count > 3 ? $" · +{count - 3} more" : "");
            segments.Add(new TimelineSegment(
                TimelineSegmentKind.Remaining, null, RemainingColor, remaining, false,
                title, sub, loggedShare, remainingShare, capped));
        }

        var pins = BuildPins(stopwatches, pieces, starts, widths, byId);
        return new TodayTimeline(
            segments, loggedShare, pins.Pins, pins.MaxLevel, logged, remaining, remainingGoals, capped);
    }

    private static (IReadOnlyList<TimelineGoalPin> Pins, int MaxLevel) BuildPins(
        IReadOnlyList<StopwatchDefinition> stopwatches,
        List<(Session Session, TimeSpan Duration)> pieces,
        List<double> starts,
        List<double> widths,
        Dictionary<string, StopwatchDefinition> byId)
    {
        var raw = new List<(StopwatchDefinition Def, double Position, string Sub)>();
        foreach (var def in stopwatches.Where(s => !s.IsDeleted && s.HasGoal))
        {
            var goal = def.Goal!.Value;
            var cumulative = TimeSpan.Zero;
            var loggedBefore = TimeSpan.Zero;
            var ordinal = 0;
            for (var i = 0; i < pieces.Count; i++)
            {
                var (session, duration) = pieces[i];
                if (session.StopwatchId != def.Id)
                {
                    loggedBefore += duration;
                    continue;
                }

                ordinal++;
                if (cumulative + duration >= goal && duration > TimeSpan.Zero)
                {
                    var offset = goal - cumulative;
                    var position = starts[i] + widths[i] * (offset / duration);
                    var into = loggedBefore + offset;
                    var live = session.IsOpen ? " (running now)" : "";
                    var sub = $"During its {TimeFormat.Ordinal(ordinal)} session{live} · {TimeFormat.HoursMinutes(into)} into today's log";
                    raw.Add((def, position, sub));
                    break;
                }

                cumulative += duration;
                loggedBefore += duration;
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

    private static string RemainingText(TimeSpan value) =>
        value < TimeSpan.FromSeconds(30) ? TimeFormat.Human(value) : TimeFormat.HoursMinutes(value);

    private static TimeSpan Sum(IEnumerable<TimeSpan> values) =>
        values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
