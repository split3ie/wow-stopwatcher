using WowStopwatcher.App.Composition;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Detects that the computer slept or lost power while stopwatches ran (the engine stops them at the last-alive time)
/// and asks whether to resume them. Ticks the engine itself so this works with Today off or a busy UI thread.
/// </summary>
public sealed class StopwatchInterruptionService(
    IStopwatchState state,
    IStopwatchControl control,
    IStopwatchMaintenance maintenance,
    IUiDispatcher dispatcher,
    IMainWindowService mainWindow,
    IDialogService dialog,
    TimeProvider time) : IAppService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    private ITimer? _timer;

    public void Start()
    {
        state.Changed += OnChanged;
        _timer = time.CreateTimer(_ => OnTimer(), null, TickInterval, TickInterval);
        CheckForInterruption();
    }

    public void Dispose()
    {
        state.Changed -= OnChanged;
        _timer?.Dispose();
    }

    private void OnTimer()
    {
        maintenance.Tick();
        CheckForInterruption();
    }

    // Today's own tick may detect the gap first, and Changed can fire on any thread.
    private void OnChanged(object? sender, EventArgs e) => CheckForInterruption();

    private void CheckForInterruption()
    {
        if (maintenance.TakeInterruption() is { } interruption)
        {
            dispatcher.Post(() => Ask(interruption));
        }
    }

    private void Ask(StopwatchInterruption interruption)
    {
        var snapshot = state.GetSnapshot();
        var names = string.Join(", ", interruption.StopwatchIds.Select(id => snapshot.Find(id)?.Name ?? id));
        var local = TimeZoneInfo.ConvertTime(interruption.StoppedAtUtc, time.LocalTimeZone);
        var today = LocalDays.ToLocalDate(time.GetUtcNow(), time.LocalTimeZone);
        // "t" alone is the short time pattern; inside a custom format it would be the AM/PM designator.
        var shown = local.ToString("t", Loc.Culture);
        if (DateOnly.FromDateTime(local.DateTime) != today)
        {
            shown = $"{local.ToString("d MMMM", Loc.Culture)}, {shown}";
        }

        mainWindow.Show();
        dialog.Show(
            Loc.T("interruption.title"),
            Loc.T("interruption.body", ("time", shown), ("names", names)),
            Loc.T("interruption.resume"),
            () => Resume(interruption.StopwatchIds));
    }

    // Normal stopwatches first: starting a normal one stops the running normal one but leaves independent ones alone.
    private void Resume(IReadOnlyList<string> ids)
    {
        var definitions = state.GetSnapshot().AllStopwatches;
        foreach (var id in ids.OrderBy(id => definitions.Any(d => d.Id == id && d.IsIndependent)))
        {
            control.Start(id);
        }
    }
}
