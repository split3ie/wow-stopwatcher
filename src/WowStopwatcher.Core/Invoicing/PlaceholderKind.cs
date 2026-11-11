namespace WowStopwatcher.Core.Invoicing;

/// <summary>How a placeholder gets its value.</summary>
public enum PlaceholderKind
{
    /// <summary>The value is used as is.</summary>
    Static,

    /// <summary>The value is a script that is evaluated to produce the result.</summary>
    Script,

    /// <summary>The built-in value of the alias is used; only meaningful for a built-in alias that is not a line marker.</summary>
    Automatic,
}
