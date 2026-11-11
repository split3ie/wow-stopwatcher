namespace WowStopwatcher.Core.Invoicing;

/// <summary>Runs placeholder scripts. The implementation lives outside Core.</summary>
public interface IScriptEvaluator
{
    /// <summary>Evaluates <paramref name="script"/> and returns its value as text.</summary>
    /// <exception cref="ScriptEvaluationException">The script failed; the message is user-readable.</exception>
    string Evaluate(string script, ScriptContext context);
}
