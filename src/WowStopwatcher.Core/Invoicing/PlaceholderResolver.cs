namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// Resolves user placeholders. Static values are used as is; scripts run lazily and may read other placeholders.
/// </summary>
public static class PlaceholderResolver
{
    /// <summary>
    /// Resolves every definition. Results are memoized; a cycle gives each alias in it an error "Cycle: a → b → a";
    /// a failing script gives an error with the exception message; a script reading an unknown alias gets null.
    /// <paramref name="baseContext"/>'s <see cref="ScriptContext.Placeholder"/> is ignored.
    /// </summary>
    public static IReadOnlyDictionary<string, PlaceholderResult> Resolve(
        IReadOnlyList<PlaceholderDefinition> definitions,
        ScriptContext baseContext,
        IScriptEvaluator evaluator)
    {
        var byAlias = new Dictionary<string, PlaceholderDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition.Kind == PlaceholderKind.Automatic)
            {
                continue;
            }

            byAlias.TryAdd(definition.Alias, definition);
        }

        var results = new Dictionary<string, PlaceholderResult>(StringComparer.Ordinal);
        var cycleErrors = new Dictionary<string, string>(StringComparer.Ordinal);
        var stack = new List<string>();

        PlaceholderResult ResolveOne(string alias)
        {
            if (results.TryGetValue(alias, out var done))
            {
                return done;
            }

            var definition = byAlias[alias];
            if (definition.Kind == PlaceholderKind.Static)
            {
                return results[alias] = new PlaceholderResult(definition.Value, null);
            }

            stack.Add(alias);
            PlaceholderResult result;
            try
            {
                var context = baseContext with { Placeholder = Lookup };
                result = new PlaceholderResult(evaluator.Evaluate(definition.Value, context), null);
            }
            catch (ScriptEvaluationException ex)
            {
                result = new PlaceholderResult(null, ex.Message, ex.Code);
            }
            finally
            {
                stack.RemoveAt(stack.Count - 1);
            }

            if (cycleErrors.TryGetValue(alias, out var cycle))
            {
                result = new PlaceholderResult(null, cycle, ScriptErrorCode.Cycle);
            }

            return results[alias] = result;
        }

        string? Lookup(string alias)
        {
            if (!byAlias.ContainsKey(alias))
            {
                return null;
            }

            var at = stack.IndexOf(alias);
            if (at >= 0)
            {
                var chain = stack.Skip(at).Append(alias).ToList();
                var message = "Cycle: " + string.Join(" → ", chain);
                foreach (var member in chain)
                {
                    cycleErrors[member] = message;
                }

                return null;
            }

            return ResolveOne(alias).Value;
        }

        foreach (var alias in byAlias.Keys)
        {
            ResolveOne(alias);
        }

        return results;
    }
}
