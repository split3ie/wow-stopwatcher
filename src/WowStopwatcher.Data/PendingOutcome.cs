namespace WowStopwatcher.Data;

/// <summary>Result of a pending step run at startup, for the UI to show once.</summary>
/// <param name="Kind">The step that was run.</param>
/// <param name="Error">Null on success.</param>
/// <param name="Message">Technical detail on failure (English), for the error text and logs.</param>
public sealed record PendingOutcome(PendingKind Kind, TransferErrorCode? Error, string? Message)
{
    /// <summary>True when the step completed.</summary>
    public bool Success => Error is null;
}
