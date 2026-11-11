using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Runs a save action a short time after the last edit, on the UI thread; <see cref="Flush"/> runs a pending save at once.</summary>
internal sealed class InvoicingSaveDebouncer
{
    private readonly Action _save;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _timer;
    private bool _pending;

    public InvoicingSaveDebouncer(Action save, IUiDispatcher dispatcher, TimeSpan delay)
    {
        _save = save;
        _dispatcher = dispatcher;
        _delay = delay;
    }

    /// <summary>Restarts the delay; the save runs when it passes without another call.</summary>
    public void Schedule()
    {
        _timer?.Cancel();
        _pending = true;
        var timer = _timer = new CancellationTokenSource();
        _ = Task.Delay(_delay, timer.Token).ContinueWith(
            _ => _dispatcher.Post(Run),
            timer.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    /// <summary>Runs the save now when an edit is waiting.</summary>
    public void Flush()
    {
        _timer?.Cancel();
        Run();
    }

    private void Run()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        _save();
    }
}
