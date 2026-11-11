namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Derives the live days of an invoice from sessions. Sessions are never changed.
/// </summary>
public static class InvoiceLineBuilder
{
    /// <summary>
    /// Builds one live day per local date (of the session start) in <paramref name="period"/> that has billed time.
    /// A line is one stopwatch's time that day rounded to whole minutes (away from zero); zero-minute lines are dropped.
    /// Lines are ordered by the stopwatch's first use that day. An open session counts up to <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="sessions">Sessions of any stopwatches.</param>
    /// <param name="stopwatchNames">Names to snapshot, by stopwatch id; a missing id falls back to the id.</param>
    /// <param name="assignedStopwatchIds">Only these stopwatches are billed.</param>
    /// <param name="period">Only days inside it are returned.</param>
    /// <param name="zone">Zone that decides the local date.</param>
    /// <param name="nowUtc">The current time.</param>
    public static IReadOnlyList<InvoiceDay> Build(
        IEnumerable<Session> sessions,
        IReadOnlyDictionary<string, string> stopwatchNames,
        IReadOnlySet<string> assignedStopwatchIds,
        InvoicePeriod period,
        TimeZoneInfo zone,
        DateTimeOffset nowUtc)
    {
        var days = new SortedDictionary<DateOnly, DayAccumulator>();

        foreach (var session in sessions.Where(s => assignedStopwatchIds.Contains(s.StopwatchId)).OrderBy(s => s.StartUtc))
        {
            var date = LocalDays.ToLocalDate(session.StartUtc, zone);
            if (!period.Contains(date))
            {
                continue;
            }

            if (!days.TryGetValue(date, out var day))
            {
                day = new DayAccumulator();
                days[date] = day;
            }

            day.Add(session.StopwatchId, session.GetDuration(nowUtc), session.IsOpen);
        }

        var result = new List<InvoiceDay>();
        foreach (var (date, accumulator) in days)
        {
            var lines = new List<InvoiceLine>();
            foreach (var (stopwatchId, duration) in accumulator.Totals)
            {
                var minutes = (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero);
                if (minutes <= 0)
                {
                    continue;
                }

                var name = stopwatchNames.TryGetValue(stopwatchId, out var n) ? n : stopwatchId;
                lines.Add(new InvoiceLine(date, minutes, string.Empty, [new InvoiceLineSource(stopwatchId, name)]));
            }

            if (lines.Count == 0)
            {
                continue;
            }

            result.Add(new InvoiceDay(date, false, lines, lines.Sum(l => l.Minutes), accumulator.HasRunning));
        }

        return result;
    }

    private sealed class DayAccumulator
    {
        private readonly List<string> _order = [];
        private readonly Dictionary<string, TimeSpan> _totals = [];

        public bool HasRunning { get; private set; }

        public IEnumerable<KeyValuePair<string, TimeSpan>> Totals =>
            _order.Select(id => new KeyValuePair<string, TimeSpan>(id, _totals[id]));

        public void Add(string stopwatchId, TimeSpan duration, bool isOpen)
        {
            if (_totals.TryGetValue(stopwatchId, out var current))
            {
                _totals[stopwatchId] = current + duration;
            }
            else
            {
                _order.Add(stopwatchId);
                _totals[stopwatchId] = duration;
            }

            HasRunning |= isOpen;
        }
    }
}
