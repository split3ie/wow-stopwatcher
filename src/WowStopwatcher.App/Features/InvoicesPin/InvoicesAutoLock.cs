using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Locks Invoices when the main window is hidden and after the idle time (checked on a timer).</summary>
public sealed class InvoicesAutoLock : IAppService, IWindowVisibilityAware
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private readonly InvoicesPinService _pin;
    private readonly TimeProvider _time;
    private readonly IUiDispatcher _dispatcher;
    private ITimer? _timer;

    public InvoicesAutoLock(InvoicesPinService pin, TimeProvider time, IUiDispatcher dispatcher)
    {
        _pin = pin;
        _time = time;
        _dispatcher = dispatcher;
    }

    public void Start() => _timer ??= _time.CreateTimer(_ => _dispatcher.Post(CheckIdle), null, CheckInterval, CheckInterval);

    public void SetWindowVisible(bool visible)
    {
        if (!visible)
        {
            _pin.LockState.Lock();
        }
    }

    /// <summary>Locks the page when the idle time has run out.</summary>
    public void CheckIdle() => _pin.LockState.CheckIdle();

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
