namespace WowStopwatcher.Core.Invoicing;

/// <summary>How a placeholder gets its value.</summary>
public enum PlaceholderKind
{
    /// <summary>The value is used as is.</summary>
    Static,

    /// <summary>The value is a script that is evaluated to produce the result.</summary>
    Script,
}
