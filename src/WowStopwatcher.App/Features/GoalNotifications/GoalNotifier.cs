using Avalonia.Threading;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary>
/// Shows the goal-reached popup when a stopwatch crosses its daily goal while the app runs. Goals already reached at start
/// are not announced. Must be created and used on the UI thread.
/// </summary>
public sealed class GoalNotifier : IAppService
{
    private readonly IStopwatchState _state;
    private readonly IGoalNotificationSettings _settings;
    private readonly IMainWindowService _mainWindow;
    private readonly IUiDispatcher _dispatcher;
    private readonly GoalTracker _tracker = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private GoalReachedWindow? _window;
    private bool _disposed;

    public GoalNotifier(IStopwatchState state, IGoalNotificationSettings settings, IMainWindowService mainWindow, IUiDispatcher dispatcher)
    {
        _state = state;
        _settings = settings;
        _mainWindow = mainWindow;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _tracker.Check(_state.GetSnapshot());
        _timer.Tick += (_, _) => Check();
        _timer.Start();
        _state.Changed += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e) => _dispatcher.Post(Check);

    private void Check()
    {
        if (_disposed)
        {
            return;
        }

        // Always advance the tracker, so goals reached while notifications are off are not announced when they are turned on.
        var reached = _tracker.Check(_state.GetSnapshot());
        if (reached.Count == 0 || !_settings.IsEnabled)
        {
            return;
        }

        if (_window is null)
        {
            var window = new GoalReachedWindow().Attach(_mainWindow.Show);
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
        _state.Changed -= OnStateChanged;
        _timer.Stop();
        _window?.Close();
        _window = null;
    }
}
