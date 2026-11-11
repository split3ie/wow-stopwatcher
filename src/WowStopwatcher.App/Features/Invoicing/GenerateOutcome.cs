namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Outcome of generating an invoice file.</summary>
public enum GenerateOutcome
{
    /// <summary>The file was written and recorded.</summary>
    Generated,

    /// <summary>Not started: see the block reasons.</summary>
    Blocked,

    /// <summary>Nothing was generated: no line has a note, so no time is billed.</summary>
    NothingAllocated,

    /// <summary>The destination file is open elsewhere and could not be replaced.</summary>
    FileInUse,

    /// <summary>The conversion or the file move failed; see the error code and detail.</summary>
    Failed,
}
