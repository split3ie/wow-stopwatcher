namespace WowStopwatcher.BuildTool.Build;

// When a new feature flag is added to src/WowStopwatcher.App/Features.props, add an entry here:
// give it a group (an existing one below, or a new FeatureGroup) and list the flags it requires, if any.
// The tool warns on startup when this list and Features.props differ.

/// <summary>The hard-coded list of optional features offered by the tool.</summary>
public static class FeatureCatalog
{
    private static readonly FeatureGroup Pages =
        new("Pages", "Pages", RequireAtLeastOne: true, AtLeastOneMessage: "Select at least one page");

    private static readonly FeatureGroup Stopwatches = new("Stopwatches", "Stopwatches");

    private static readonly FeatureGroup SystemGroup = new("System", "System");

    /// <summary>All known features, in display order (grouped by <see cref="FeatureDefinition.Group"/>).</summary>
    public static IReadOnlyList<FeatureDefinition> All { get; } =
    [
        new("FeatureToday", "Today", "Today page: timeline, stopwatch rows, totals, goals card", Pages),
        new("FeatureStatistics", "Statistics", "The Statistics screen with per-day timelines and totals.", Pages),
        new("FeatureSettings", "Settings", "Settings page: stopwatches, colors, goals, shortcuts, general options", Pages),
        new("FeatureInvoicing", "Invoices", "Invoices page: companies, DOCX templates, monthly PDF invoices, Invoice button in Statistics", Pages),

        new("FeatureIndependent", "Independent", "Stopwatches that run alongside the others", Stopwatches),
        new("FeatureGoals", "Goals", "Daily goals, goal progress and the goals card", Stopwatches),
        new("FeaturePermanent", "Permanent",
            "Permanent stopwatches; when off, every stopwatch is temporary and hides after a full day unused", Stopwatches),

        new("FeatureTray", "Tray", "Tray icon with a menu of stopwatches.", SystemGroup),
        new("FeatureHotkeys", "Hotkeys", "Keyboard shortcuts that start or stop stopwatches in the background.", SystemGroup),
        new("FeatureGoalNotifications", "Goal notifications", "Popup when a stopwatch reaches its daily goal.", SystemGroup,
            ["FeatureGoals"]),
        new("FeatureAutostart", "Autostart", "Start the app when the user signs in.", SystemGroup, ["FeatureSettings"]),
        new("FeatureUpdateCheck", "Update check", "Check GitHub for a newer version.", SystemGroup, ["FeatureSettings"]),
    ];

    /// <summary>The groups in display order.</summary>
    public static IReadOnlyList<FeatureGroup> Groups { get; } = All.Select(f => f.Group).Distinct().ToList();
}
