namespace WowStopwatcher.Core;

/// <summary>Picks the shortcut a restored stopwatch gets.</summary>
internal static class RestoredShortcut
{
    /// <summary>Keeps the shortcut unless a visible stopwatch already uses it (case-insensitive); then the next free default.</summary>
    public static string? Resolve(string? shortcut, IReadOnlyCollection<StopwatchDefinition> visible)
    {
        if (!string.IsNullOrWhiteSpace(shortcut)
            && visible.Any(d => string.Equals(d.Shortcut?.Trim(), shortcut.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return DefaultShortcuts.Next(visible.Select(d => d.Shortcut));
        }

        return shortcut;
    }
}
