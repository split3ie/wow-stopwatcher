namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// One stopwatch's share of one invoice day: noted entries are billed, the rest of the tracked time is unallocated and not billed.
/// Immutable; every operation returns a new allocation and throws <see cref="ArgumentException"/> on invalid input.
/// </summary>
public sealed class StopwatchDayAllocation
{
    private StopwatchDayAllocation(
        string stopwatchId,
        string stopwatchName,
        int trackedMinutes,
        IReadOnlyList<Entry> entries,
        IReadOnlyList<InvoiceLine> mergedLines)
    {
        StopwatchId = stopwatchId;
        StopwatchName = stopwatchName;
        TrackedMinutes = trackedMinutes;
        Entries = entries;
        MergedLines = mergedLines;
    }

    /// <summary>A noted (billed) part of the stopwatch's day.</summary>
    /// <param name="Minutes">Minutes of the entry.</param>
    /// <param name="Note">Non-empty trimmed note.</param>
    public sealed record Entry(int Minutes, string Note);

    /// <summary>Stopwatch id.</summary>
    public string StopwatchId { get; }

    /// <summary>Stopwatch name used for new lines.</summary>
    public string StopwatchName { get; }

    /// <summary>Minutes the stopwatch actually ran that day.</summary>
    public int TrackedMinutes { get; }

    /// <summary>Single-source lines with a note, in order.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Tracked time not covered by entries; never billed.</summary>
    public int Unallocated => Math.Max(0, TrackedMinutes - AllocatedMinutes);

    /// <summary>Entries beyond the tracked time (for example 7:58 rounded up to 8:00).</summary>
    public int OverMinutes => Math.Max(0, AllocatedMinutes - TrackedMinutes);

    /// <summary>Sum of entries; this is what is billed.</summary>
    public int BilledMinutes => AllocatedMinutes;

    /// <summary>Billed minus tracked.</summary>
    public int DifferenceMinutes => BilledMinutes - TrackedMinutes;

    /// <summary>Lines of several stopwatches that include this one; read-only and not part of <see cref="BilledMinutes"/>.</summary>
    public IReadOnlyList<InvoiceLine> MergedLines { get; }

    private int AllocatedMinutes => Entries.Sum(e => e.Minutes);

    /// <summary>Builds the allocation of one stopwatch from the day's lines; its un-noted lines carry no minutes into the result.</summary>
    public static StopwatchDayAllocation From(IReadOnlyList<InvoiceLine> dayLines, string stopwatchId, string stopwatchName, int trackedMinutes)
    {
        var entries = new List<Entry>();
        var merged = new List<InvoiceLine>();
        foreach (var line in dayLines)
        {
            if (IsOwn(line, stopwatchId))
            {
                var note = line.Note.Trim();
                if (note.Length > 0)
                {
                    entries.Add(new Entry(line.Minutes, note));
                }
            }
            else if (line.IsMerged && line.Sources.Any(s => s.StopwatchId == stopwatchId))
            {
                merged.Add(line);
            }
        }

        return new StopwatchDayAllocation(stopwatchId, stopwatchName, trackedMinutes, entries, merged);
    }

    /// <summary>Adds a noted entry; requires minutes &gt; 0 and a non-empty note. May exceed the tracked time.</summary>
    public StopwatchDayAllocation Allocate(int minutes, string? note)
    {
        var trimmed = RequireNote(note);
        RequirePositive(minutes);
        return With([.. Entries, new Entry(minutes, trimmed)]);
    }

    /// <summary>Changes the entry at <paramref name="index"/>; requires minutes &gt; 0 and a non-empty note.</summary>
    public StopwatchDayAllocation Edit(int index, int minutes, string? note)
    {
        GetEntry(index);
        var trimmed = RequireNote(note);
        RequirePositive(minutes);
        var entries = Entries.ToList();
        entries[index] = new Entry(minutes, trimmed);
        return With(entries);
    }

    /// <summary>Removes the entry at <paramref name="index"/>.</summary>
    public StopwatchDayAllocation Delete(int index)
    {
        GetEntry(index);
        var entries = Entries.ToList();
        entries.RemoveAt(index);
        return With(entries);
    }

    /// <summary>
    /// Returns the day's new lines: other lines stay in place; this stopwatch's single-source lines are replaced at the
    /// position of the first of them (or appended) by the entries and then one unallocated line when there are minutes left.
    /// </summary>
    public IReadOnlyList<InvoiceLine> ApplyTo(IReadOnlyList<InvoiceLine> dayLines, DateOnly date)
    {
        InvoiceLineSource[] sources = [new InvoiceLineSource(StopwatchId, StopwatchName)];
        var replacement = Entries.Select(e => new InvoiceLine(date, e.Minutes, e.Note, sources)).ToList();
        if (Unallocated > 0)
        {
            replacement.Add(new InvoiceLine(date, Unallocated, string.Empty, sources));
        }

        // Time moved in from other days stays on the first replacement line.
        var moves = InvoiceLineMoves.Combine(dayLines.Where(l => IsOwn(l, StopwatchId)).SelectMany(l => l.MovedIn));
        if (moves.Count > 0 && replacement.Count > 0)
        {
            replacement[0] = replacement[0] with { MovedIn = moves };
        }

        var result = new List<InvoiceLine>();
        var placed = false;
        foreach (var line in dayLines)
        {
            if (!IsOwn(line, StopwatchId))
            {
                result.Add(line);
            }
            else if (!placed)
            {
                result.AddRange(replacement);
                placed = true;
            }
        }

        if (!placed)
        {
            result.AddRange(replacement);
        }

        return result;
    }

    private static bool IsOwn(InvoiceLine line, string stopwatchId) =>
        line.Sources.Count == 1 && line.Sources[0].StopwatchId == stopwatchId;

    private static string RequireNote(string? note)
    {
        var trimmed = (note ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("The note must not be empty.", nameof(note));
        }

        return trimmed;
    }

    private Entry GetEntry(int index)
    {
        if (index < 0 || index >= Entries.Count)
        {
            throw new ArgumentException($"Entry index {index} is out of range (0 to {Entries.Count - 1}).", nameof(index));
        }

        return Entries[index];
    }

    private static void RequirePositive(int minutes)
    {
        if (minutes <= 0)
        {
            throw new ArgumentException($"Minutes must be more than 0, but was {minutes} min.", nameof(minutes));
        }
    }

    private StopwatchDayAllocation With(IReadOnlyList<Entry> entries) =>
        new(StopwatchId, StopwatchName, TrackedMinutes, entries, MergedLines);
}
