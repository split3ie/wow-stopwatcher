namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Outcome of marking an invoice as sent.</summary>
public enum MarkSentResult
{
    /// <summary>The invoice is now sent.</summary>
    Sent,

    /// <summary>Lines or values changed since the file was generated; nothing was changed. Ask, then retry with confirmation.</summary>
    ChangedSinceGeneration,

    /// <summary>The invoice was already sent; nothing was changed.</summary>
    AlreadySent,
}
