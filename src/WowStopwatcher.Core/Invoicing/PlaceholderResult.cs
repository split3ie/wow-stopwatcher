namespace WowStopwatcher.Core.Invoicing;

/// <summary>The outcome of resolving one placeholder: a value, or an error.</summary>
/// <param name="Value">The result; null when there is an error.</param>
/// <param name="Error">The error detail; null when it worked.</param>
/// <param name="ErrorCode">Why it failed, for localized text; null when it worked.</param>
public sealed record PlaceholderResult(string? Value, string? Error, ScriptErrorCode? ErrorCode = null)
{
    /// <summary>True when the placeholder has a value.</summary>
    public bool IsOk => Error is null;
}
