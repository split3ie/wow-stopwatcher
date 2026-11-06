namespace WowStopwatcher.Core;

/// <summary>
/// A configured stopwatch. Deleted stopwatches are kept (soft delete) so their sessions stay in statistics.
/// </summary>
/// <param name="Id">Stable identifier (see <see cref="NewId"/>).</param>
/// <param name="Name">Display name, for example "English".</param>
/// <param name="Color">Hex color such as "#2F6FEB".</param>
/// <param name="Goal">Optional daily goal; null means no goal.</param>
/// <param name="Shortcut">Optional global shortcut text such as "Ctrl+F1" or "Ctrl+Alt+Shift+K".</param>
/// <param name="SortOrder">Position in lists; lower comes first.</param>
/// <param name="IsDeleted">True when the stopwatch was deleted in Settings.</param>
public sealed record StopwatchDefinition(
    string Id,
    string Name,
    string Color,
    TimeSpan? Goal,
    string? Shortcut,
    int SortOrder,
    bool IsDeleted = false)
{
    /// <summary>True when a positive daily goal is set.</summary>
    public bool HasGoal => Goal is { } goal && goal > TimeSpan.Zero;

    /// <summary>Creates a new unique stopwatch id.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
