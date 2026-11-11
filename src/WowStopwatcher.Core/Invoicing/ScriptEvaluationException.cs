namespace WowStopwatcher.Core.Invoicing;

/// <summary>A script failed. <see cref="Code"/> says why; the message is the detail (for example the engine's text).</summary>
public sealed class ScriptEvaluationException : Exception
{
    /// <summary>Creates a <see cref="ScriptErrorCode.Runtime"/> error with a detail message.</summary>
    public ScriptEvaluationException(string message)
        : this(ScriptErrorCode.Runtime, message)
    {
    }

    /// <summary>Creates the exception with a code, a detail message and optionally the cause.</summary>
    public ScriptEvaluationException(ScriptErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Why the script failed.</summary>
    public ScriptErrorCode Code { get; }
}
