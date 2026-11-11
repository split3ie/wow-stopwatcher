namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Derives the live days of an invoice from sessions. Sessions are never changed.
/// </summary>
public static class InvoiceLineBuilder
{
    /// <summary>
    /// Builds one live day per invoice day (see <see cref="InvoiceDayRanges"/>) of <paramref name="period"/> that has billed time.
    /// Each session is clipped to the day ranges, so a session split at midnight joins back into the day it belongs to.
    /// A line is one stopwatch's time that day rounded to whole minutes (away from zero); zero-minute lines are dropped.
    /// Lines are ordered by the stopwatch's first use that day. An open session counts up to <paramref name="nowUtc"/>.
    /// </summary>
    /// <param name="sessions">Sessions of any stopwatches.</param>
    /// <param name="stopwatchNames">Names to snapshot, by stopwatch id; a missing id falls back to the id.</param>
    /// <param name="assignedStopwatchIds">Only these stopwatches are billed.</param>
    /// <param name="period">Only days inside it are returned.</param>
    /// <param name="zone">Zone that decides the local date and time.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="defaultDayStartMinutes">The company's default day start, minutes after midnight; 0 gives calendar days.</param>
    /// <param name="dayStartOverrides">Day starts set for single dates, including the day after the period's last day; may be null.</param>
    public static IReadOnlyList<InvoiceDay> Build(
        IEnumerable<Session> sessions,
        IReadOnlyDictionary<string, string> stopwatchNames,
        IReadOnlySet<string> assignedStopwatchIds,
        InvoicePeriod period,
        TimeZoneInfo zone,
        DateTimeOffset nowUtc,
        int defaultDayStartMinutes = 0,
        IReadOnlyDictionary<DateOnly, int>? dayStartOverrides = null)
    {
        var ranges = InvoiceDayRanges.ForPeriod(period, defaultDayStartMinutes, dayStartOverrides, zone);
        var days = new SortedDictionary<DateOnly, DayAccumulator>();

        foreach (var session in sessions.Where(s => assignedStopwatchIds.Contains(s.StopwatchId)).OrderBy(s => s.StartUtc))
        {
            var sessionEnd = session.StartUtc + session.GetDuration(nowUtc);
            foreach (var range in ranges)
            {
                var from = session.StartUtc > range.StartUtc ? session.StartUtc : range.StartUtc;
                var to = sessionEnd < range.EndUtc ? sessionEnd : range.EndUtc;
                if (to <= from)
                {
                    continue;
                }

                if (!days.TryGetValue(range.Date, out var day))
                {
                    day = new DayAccumulator();
                    days[range.Date] = day;
                }

                day.Add(session.StopwatchId, to - from, session.IsOpen && nowUtc < range.EndUtc);
            }
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
