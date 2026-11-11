namespace WowStopwatcher.Core.Invoicing;

/// <summary>A stopwatch whose time a line bills; the name is a snapshot because names can change.</summary>
/// <param name="StopwatchId">Stopwatch id.</param>
/// <param name="StopwatchName">Name when the line was made.</param>
public sealed record InvoiceLineSource(string StopwatchId, string StopwatchName);
