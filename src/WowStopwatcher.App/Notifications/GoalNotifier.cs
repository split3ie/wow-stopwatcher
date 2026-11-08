using Avalonia.Threading;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Notifications;

/// <summary>
/// Shows the goal-reached popup when a stopwatch crosses its daily goal while the app runs. Goals already reached at start
/// are not announced. Must be created and used on the UI thread.
/// </summary>
public sealed class GoalNotifier : IDisposable
{
    private readonly StopwatchEngine _engine;
    private readonly ISettingsRepository _settings;
    private readonly Action _openMain;
    private readonly GoalTracker _tracker = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GoalReachedWindow? _window;
    private bool _disposed;

    public GoalNotifier(StopwatchEngine engine, ISettingsRepository settings, Action openMain)
    {
        _engine = engine;
        _settings = settings;
        _openMain = openMain;
    }

    public void Start()
    {
        _tracker.Check(_engine.GetSnapshot());
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        _engine.Changed += OnEngineChanged;
    }

    private void OnEngineChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Check);

    private void Check()
    {
        if (_disposed)
        {
            return;
        }

        // Always advance the tracker, so goals reached while notifications are off are not announced when they are turned on.
        var reached = _tracker.Check(_engine.GetSnapshot());
        if (reached.Count == 0 || !GoalNotificationSetting.IsEnabled(_settings))
        {
            return;
        }

        if (_window is null)
        {
            var window = new GoalReachedWindow().Attach(_openMain);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };
            _window = window;
            foreach (var definition in reached)
            {
                window.AddGoal(definition);
            }

            window.Show();
            return;
        }

        foreach (var definition in reached)
        {
            _window.AddGoal(definition);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _engine.Changed -= OnEngineChanged;
        _timer.Stop();
        _window?.Close();
        _window = null;
    }
}
