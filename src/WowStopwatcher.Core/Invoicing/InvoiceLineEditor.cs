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
        result[index] = line with { Minutes = firstMinutes };
        result.Insert(index + 1, line with { Minutes = line.Minutes - firstMinutes });
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
        var sources = new List<InvoiceLineSource>();
        foreach (var source in selected.SelectMany(l => l.Sources))
        {
            if (sources.All(s => s.StopwatchId != source.StopwatchId))
            {
                sources.Add(source);
            }
        }

        var note = string.Join("; ", selected.Select(l => l.Note.Trim()).Where(n => n.Length > 0).Distinct());
        var merged = selected[0] with { Minutes = selected.Sum(l => l.Minutes), Note = note, Sources = sources };

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
