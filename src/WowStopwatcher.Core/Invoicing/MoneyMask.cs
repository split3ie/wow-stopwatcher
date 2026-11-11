namespace WowStopwatcher.Core.Invoicing;

/// <summary>Hides money on screen. Display only: generated files are never masked.</summary>
public static class MoneyMask
{
    /// <summary>The text shown instead of a hidden value.</summary>
    public const string Mask = "****";

    /// <summary>Returns <paramref name="value"/>, or <see cref="Mask"/> when <paramref name="hidden"/>.</summary>
    public static string Apply(string value, bool hidden) => hidden ? Mask : value;
}
