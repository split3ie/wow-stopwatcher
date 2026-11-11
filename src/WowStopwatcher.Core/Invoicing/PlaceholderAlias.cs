using System.Text.RegularExpressions;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>Validation of user placeholder aliases.</summary>
public static partial class PlaceholderAlias
{
    /// <summary>Error code: the alias is empty.</summary>
    public const string Empty = "empty";

    /// <summary>Error code: the alias has characters outside letters, digits, '_' and '.', or does not start with a letter.</summary>
    public const string InvalidCharacters = "invalid";

    /// <summary>Error code: the alias is a built-in name (any letter case) or starts with "line.".</summary>
    public const string Reserved = "reserved";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]*$")]
    private static partial Regex ValidRegex();

    /// <summary>Returns null when <paramref name="alias"/> is acceptable, otherwise an error code.</summary>
    public static string? Validate(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return Empty;
        }

        if (!ValidRegex().IsMatch(alias))
        {
            return InvalidCharacters;
        }

        if (alias.StartsWith("line.", StringComparison.OrdinalIgnoreCase)
            || InvoiceBuiltins.All.Any(n => string.Equals(n, alias, StringComparison.OrdinalIgnoreCase)))
        {
            return Reserved;
        }

        return null;
    }
}
