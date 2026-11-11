namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// One invoice line: a day, whole minutes and one or more stopwatches.
/// </summary>
/// <param name="Date">Local day.</param>
/// <param name="Minutes">Billed minutes.</param>
/// <param name="Note">Free text; empty when none.</param>
/// <param name="Sources">Billed stopwatches; more than one after a merge; empty for a custom line.</param>
public sealed record InvoiceLine(DateOnly Date, int Minutes, string Note, IReadOnlyList<InvoiceLineSource> Sources)
{
    /// <summary>Portions of this line's time that come from other days (after merging across days); empty when none.</summary>
    public IReadOnlyList<InvoiceLineMove> MovedIn { get; init; } = [];

    /// <summary>True when the line bills several stopwatches.</summary>
    public bool IsMerged => Sources.Count > 1;

    /// <summary>True for a custom line: time added by hand, not from tracked sessions (no stopwatches).</summary>
    public bool IsCustom => Sources.Count == 0;
}
