namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// One invoice line: a day, whole minutes and one or more stopwatches.
/// </summary>
/// <param name="Date">Local day.</param>
/// <param name="Minutes">Billed minutes.</param>
/// <param name="Note">Free text; empty when none.</param>
/// <param name="Sources">Billed stopwatches; more than one after a merge.</param>
public sealed record InvoiceLine(DateOnly Date, int Minutes, string Note, IReadOnlyList<InvoiceLineSource> Sources)
{
    /// <summary>True when the line bills several stopwatches.</summary>
    public bool IsMerged => Sources.Count > 1;
}
