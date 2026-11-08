namespace WowStopwatcher.Core;

/// <summary>
/// Cuts sessions into slices: stretches of time where the set of running stopwatches is constant.
/// The sum of the slice durations is the union of all session intervals (overlapping time counts once).
/// </summary>
public static class SessionSlicer
{
    /// <param name="sessions">Sessions of any stopwatches; open ones are measured up to <paramref name="nowUtc"/>.</param>
    /// <param name="stopwatches">All definitions including deleted; sessions of unknown ids are ignored.</param>
    /// <returns>Slices in chronological order; gaps where nothing runs are omitted.</returns>
    public static IReadOnlyList<SessionSlice> Build(
        IEnumerable<Session> sessions,
        IReadOnlyList<StopwatchDefinition> stopwatches,
        DateTimeOffset nowUtc)
    {
        var byId = stopwatches.ToDictionary(s => s.Id);
        var items = sessions
            .Where(s => byId.ContainsKey(s.StopwatchId))
            .Select(s => (Session: s, Def: byId[s.StopwatchId], End: s.StartUtc + s.GetDuration(nowUtc)))
            .Where(i => i.End > i.Session.StartUtc)
            .ToList();

        var boundaries = items
            .SelectMany(i => new[] { i.Session.StartUtc, i.End })
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var slices = new List<SessionSlice>();
        for (var b = 0; b + 1 < boundaries.Count; b++)
        {
            var from = boundaries[b];
            var to = boundaries[b + 1];
            var lanes = items
                .Where(i => i.Session.StartUtc <= from && i.End >= to)
                .GroupBy(i => i.Def.Id)
                .Select(g => (Def: g.First().Def, Live: g.Any(i => i.Session.IsOpen)))
                .OrderBy(l => l.Def.IsIndependent ? 1 : 0)
                .ThenBy(l => l.Def.SortOrder)
                .ThenBy(l => l.Def.Id, StringComparer.Ordinal)
                .Select(l => new TimelineLane(l.Def.Id, l.Def.Color, l.Live))
                .ToArray();
            if (lanes.Length == 0)
            {
                continue;
            }

            if (slices.Count > 0
                && slices[^1].EndUtc == from
                && slices[^1].Lanes.Select(l => l.StopwatchId).SequenceEqual(lanes.Select(l => l.StopwatchId)))
            {
                var last = slices[^1];
                var merged = last.Lanes.Zip(lanes, (x, y) => x with { IsLive = x.IsLive || y.IsLive }).ToArray();
                slices[^1] = new SessionSlice(last.StartUtc, last.Duration + (to - from), merged);
            }
            else
            {
                slices.Add(new SessionSlice(from, to - from, lanes));
            }
        }

        return slices;
    }

    /// <summary>Sum of the slice durations.</summary>
    public static TimeSpan Total(IEnumerable<SessionSlice> slices) =>
        slices.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
}
