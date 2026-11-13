namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Outcome of checking a typed PIN.</summary>
public enum PinCheckStatus
{
    /// <summary>The PIN is right.</summary>
    Success,

    /// <summary>The PIN is wrong; some tries are left.</summary>
    Wrong,

    /// <summary>Too many wrong PINs; attempts are refused for a while.</summary>
    LockedOut,
}
