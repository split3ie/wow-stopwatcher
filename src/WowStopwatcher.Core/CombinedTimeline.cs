namespace WowStopwatcher.Core;

/// <summary>
/// Splits a day's segments into per-stopwatch pieces and groups them by stopwatch. An overlap segment gives one piece per
/// lane, so its time counts in each stopwatch's group. Groups follow the order of their first piece; pieces inside a group
/// stay chronological. Piece widths equal their segment's width.
/// </summary>
public static class CombinedTimeline
{
    /// <summary>Returns one piece per segment and lane, ordered by segment then lane.</summary>
    public static IReadOnlyList<CombinedPiece> Build(IReadOnlyList<DaySegment> segments)
    {
        var raw = new List<(int Segment, int Lane, int Count, string Id, string Color, double Start)>();
        var start = 0.0;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Lanes.Count == 0)
            {
                raw.Add((i, 0, 1, segment.StopwatchId, segment.Color, start));
            }
            else
            {
                for (var l = 0; l < segment.Lanes.Count; l++)
                {
                    raw.Add((i, l, segment.Lanes.Count, segment.Lanes[l].StopwatchId, segment.Lanes[l].Color, start));
                }
            }

            start += segment.WidthFraction;
        }

        var groupIndex = new Dictionary<string, int>();
        var members = new List<List<int>>();
        for (var p = 0; p < raw.Count; p++)
        {
            if (!groupIndex.TryGetValue(raw[p].Id, out var group))
            {
                group = members.Count;
                groupIndex[raw[p].Id] = group;
                members.Add([]);
            }

            members[group].Add(p);
        }

        var pieces = new CombinedPiece[raw.Count];
        var groupStart = 0.0;
        for (var g = 0; g < members.Count; g++)
        {
            var width = members[g].Sum(p => segments[raw[p].Segment].WidthFraction);
            var duration = TimeSpan.FromTicks(members[g].Sum(p => segments[raw[p].Segment].Duration.Ticks));
            var pieceStart = groupStart;
            for (var m = 0; m < members[g].Count; m++)
            {
                var p = members[g][m];
                var r = raw[p];
                var segment = segments[r.Segment];
                pieces[p] = new CombinedPiece(
                    r.Segment, r.Lane, r.Count, r.Id, r.Color, segment.Duration, r.Start, segment.WidthFraction,
                    pieceStart, g, groupStart, width, duration, m == members[g].Count - 1);
                pieceStart += segment.WidthFraction;
            }

            groupStart += width;
        }

        return pieces;
    }
}
