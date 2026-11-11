namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Pure edits of one day's line list. Each method returns a new list and throws <see cref="ArgumentException"/> on invalid input.
/// </summary>
public static class InvoiceLineEditor
{
    /// <summary>Splits the line at <paramref name="index"/> into two: <paramref name="firstMinutes"/> and the rest. Requires 0 &lt; first &lt; line minutes.</summary>
    public static IReadOnlyList<InvoiceLine> Split(IReadOnlyList<InvoiceLine> lines, int index, int firstMinutes)
    {
        var line = Get(lines, index);
        if (firstMinutes <= 0 || firstMinutes >= line.Minutes)
        {
            throw new ArgumentException(
                $"The first part must be more than 0 and less than the line ({line.Minutes} min), but was {firstMinutes} min.",
                nameof(firstMinutes));
        }

        var result = lines.ToList();
        // Moved-in time stays on the first part only, so it is never counted twice.
        result[index] = line with { Minutes = firstMinutes };
        result.Insert(index + 1, line with { Minutes = line.Minutes - firstMinutes, MovedIn = [] });
        return result;
    }

    /// <summary>
    /// Merges the lines at <paramref name="indices"/> (at least two, distinct) into one at the position of the first:
    /// minutes summed, sources united without duplicates, distinct non-empty notes joined with "; ".
    /// </summary>
    public static IReadOnlyList<InvoiceLine> Merge(IReadOnlyList<InvoiceLine> lines, IReadOnlyCollection<int> indices)
    {
        var ordered = indices.Distinct().Order().ToList();
        if (indices.Count != ordered.Count)
        {
            throw new ArgumentException("The indices to merge must be distinct.", nameof(indices));
        }

        if (ordered.Count < 2)
        {
            throw new ArgumentException("Merging needs at least two lines.", nameof(indices));
        }

        var selected = ordered.Select(i => Get(lines, i)).ToList();
        if (selected.Any(l => l.IsCustom))
        {
            throw new ArgumentException("A custom line cannot be merged.", nameof(indices));
        }

        var sources = new List<InvoiceLineSource>();
        foreach (var source in selected.SelectMany(l => l.Sources))
        {
            if (sources.All(s => s.StopwatchId != source.StopwatchId))
            {
                sources.Add(source);
            }
        }

        var note = string.Join("; ", selected.Select(l => l.Note.Trim()).Where(n => n.Length > 0).Distinct());
        var merged = selected[0] with { Minutes = selected.Sum(l => l.Minutes), Note = note, Sources = sources, MovedIn = InvoiceLineMoves.Combine(selected.SelectMany(l => l.MovedIn)) };

        var result = new List<InvoiceLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == ordered[0])
            {
                result.Add(merged);
            }
            else if (!ordered.Contains(i))
            {
                result.Add(lines[i]);
            }
        }

        return result;
    }

    /// <summary>True when the distinct dates form one run of consecutive days (a single day counts).</summary>
    public static bool AreNeighbouringDays(IEnumerable<DateOnly> dates)
    {
        var ordered = dates.Distinct().Order().ToList();
        return ordered.Count > 0 && ordered.Zip(ordered.Skip(1)).All(p => p.Second == p.First.AddDays(1));
    }

    /// <summary>
    /// Merges the selected lines of neighbouring days (<paramref name="selection"/> is the date and line index of each, at least two lines,
    /// no custom line, dates without a gap) into one line on <paramref name="target"/>. <paramref name="days"/> has the current lines of
    /// every selected date. Returns the new lines of each selected date, in date order; a day left without lines stays in the result.
    /// The merged line sits at the place of the first selected line of the target day; the time that came from other days
    /// is remembered in <see cref="InvoiceLine.MovedIn"/>.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, IReadOnlyList<InvoiceLine> Lines)> MergeAcrossDays(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<InvoiceLine>> days,
        IReadOnlyList<(DateOnly Date, int Index)> selection,
        DateOnly target)
    {
        if (selection.Count < 2 || selection.Distinct().Count() != selection.Count)
        {
            throw new ArgumentException("Merging needs at least two distinct lines.", nameof(selection));
        }

        var dates = selection.Select(s => s.Date).Distinct().Order().ToList();
        if (!AreNeighbouringDays(dates))
        {
            throw new ArgumentException("The days to merge must be neighbours without a gap.", nameof(selection));
        }

        if (!dates.Contains(target))
        {
            throw new ArgumentException("The target day must be one of the selected days.", nameof(target));
        }

        var selected = new List<(DateOnly Date, int Index, InvoiceLine Line)>();
        foreach (var (date, index) in selection.OrderBy(s => s.Date).ThenBy(s => s.Index))
        {
            if (!days.TryGetValue(date, out var lines))
            {
                throw new ArgumentException($"There are no lines for {date}.", nameof(days));
            }

            selected.Add((date, index, Get(lines, index)));
        }

        if (selected.Any(s => s.Line.IsCustom))
        {
            throw new ArgumentException("A custom line cannot be merged.", nameof(selection));
        }

        var sources = new List<InvoiceLineSource>();
        foreach (var source in selected.SelectMany(s => s.Line.Sources))
        {
            if (sources.All(x => x.StopwatchId != source.StopwatchId))
            {
                sources.Add(source);
            }
        }

        var moves = selected.SelectMany(s => s.Line.MovedIn).ToList();
        foreach (var (date, _, line) in selected.Where(s => s.Date != target))
        {
            moves.Add(new InvoiceLineMove(date, line.Minutes - InvoiceLineMoves.Total(line)));
        }

        var note = string.Join("; ", selected.Select(s => s.Line.Note.Trim()).Where(n => n.Length > 0).Distinct());
        var merged = selected.First(s => s.Date == target).Line with
        {
            Minutes = selected.Sum(s => s.Line.Minutes),
            Note = note,
            Sources = sources,
            MovedIn = InvoiceLineMoves.Combine(moves, target),
        };

        var result = new List<(DateOnly Date, IReadOnlyList<InvoiceLine> Lines)>();
        foreach (var date in dates)
        {
            var removed = selected.Where(s => s.Date == date).Select(s => s.Index).ToHashSet();
            var current = days[date];
            var next = new List<InvoiceLine>();
            for (var i = 0; i < current.Count; i++)
            {
                if (date == target && i == removed.Min())
                {
                    next.Add(merged);
                }
                else if (!removed.Contains(i))
                {
                    next.Add(current[i]);
                }
            }

            result.Add((date, next));
        }

        return result;
    }

    /// <summary>Appends a custom line (no stopwatches) to the day; <paramref name="minutes"/> must be positive.</summary>
    public static IReadOnlyList<InvoiceLine> AddCustom(IReadOnlyList<InvoiceLine> lines, DateOnly date, int minutes, string? note)
    {
        if (minutes <= 0)
        {
            throw new ArgumentException($"Minutes must be more than 0, but was {minutes}.", nameof(minutes));
        }

        return [.. lines, new InvoiceLine(date, minutes, (note ?? string.Empty).Trim(), [])];
    }

    /// <summary>Removes the custom line at <paramref name="index"/>; any other line is rejected.</summary>
    public static IReadOnlyList<InvoiceLine> DeleteCustom(IReadOnlyList<InvoiceLine> lines, int index)
    {
        if (!Get(lines, index).IsCustom)
        {
            throw new ArgumentException("Only a custom line can be deleted.", nameof(index));
        }

        var result = lines.ToList();
        result.RemoveAt(index);
        return result;
    }

    /// <summary>Sets the minutes of the line at <paramref name="index"/>; must be positive.</summary>
    public static IReadOnlyList<InvoiceLine> Adjust(IReadOnlyList<InvoiceLine> lines, int index, int minutes)
    {
        var line = Get(lines, index);
        if (minutes <= 0)
        {
            throw new ArgumentException($"Minutes must be more than 0, but was {minutes}.", nameof(minutes));
        }

        var result = lines.ToList();
        result[index] = line with { Minutes = minutes };
        return result;
    }

    /// <summary>Sets the trimmed note of the line at <paramref name="index"/>.</summary>
    public static IReadOnlyList<InvoiceLine> SetNote(IReadOnlyList<InvoiceLine> lines, int index, string? note)
    {
        var line = Get(lines, index);
        var result = lines.ToList();
        result[index] = line with { Note = (note ?? string.Empty).Trim() };
        return result;
    }

    private static InvoiceLine Get(IReadOnlyList<InvoiceLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count)
        {
            throw new ArgumentException($"Line index {index} is out of range (0 to {lines.Count - 1}).", nameof(index));
        }

        return lines[index];
    }
}
