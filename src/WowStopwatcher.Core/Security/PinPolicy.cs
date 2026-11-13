namespace WowStopwatcher.Core.Security;

/// <summary>Rules for the Invoices PIN: 6 to 8 ASCII digits for a new PIN. Stored PINs of 4 or 5 digits from earlier versions still verify (<see cref="PinHash.Verify"/> does not apply this policy).</summary>
public static class PinPolicy
{
    /// <summary>Fewest digits in a new PIN.</summary>
    public const int MinLength = 6;

    /// <summary>Most digits in a PIN.</summary>
    public const int MaxLength = 8;

    /// <summary>True when <paramref name="pin"/> is 6 to 8 characters, all ASCII digits '0'..'9'.</summary>
    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(c => c is >= '0' and <= '9');
}
