namespace WowStopwatcher.Core.Invoicing;

/// <summary>Why a placeholder script failed; the UI shows localized text for the code plus the detail.</summary>
public enum ScriptErrorCode
{
    /// <summary>The script threw or used an unknown name.</summary>
    Runtime,

    /// <summary>The script has a syntax error.</summary>
    Syntax,

    /// <summary>The script ran too long.</summary>
    Timeout,

    /// <summary>The script used too much memory, too many statements or too deep recursion.</summary>
    Limit,

    /// <summary>Placeholders read each other in a cycle; the detail names the chain.</summary>
    Cycle,
}
