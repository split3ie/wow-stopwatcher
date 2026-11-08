namespace WowStopwatcher.Core;

/// <summary>
/// Picks the default shortcut for a new stopwatch: the first free one of Ctrl+F1..F12, Ctrl+Shift+F1..F12,
/// Ctrl+Alt+F1..F12, Ctrl+Alt+Shift+F1..F12 (stored text format, modifiers in the order Ctrl, Alt, Shift).
/// </summary>
public static class DefaultShortcuts
{
    private static readonly string[] Prefixes = ["Ctrl", "Ctrl+Shift", "Ctrl+Alt", "Ctrl+Alt+Shift"];

    /// <summary>The first default shortcut not in <paramref name="used"/> (case-insensitive), or null when all are taken.</summary>
    public static string? Next(IEnumerable<string?> used)
    {
        var taken = used.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var prefix in Prefixes)
        {
            for (var n = 1; n <= 12; n++)
            {
                var candidate = $"{prefix}+F{n}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
