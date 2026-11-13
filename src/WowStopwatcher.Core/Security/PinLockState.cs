namespace WowStopwatcher.Core.Security;

/// <summary>
/// Locked or unlocked state of the Invoices page. Starts locked. An unlocked page locks after <see cref="IdleTimeout"/> without
/// <see cref="RecordActivity"/>; <see cref="IsLocked"/> reflects that at once, and <see cref="CheckIdle"/> applies it and raises
/// <see cref="Changed"/>. The UI calls <see cref="Lock"/> when the window is hidden. In memory only.
/// </summary>
public sealed class PinLockState(TimeProvider time)
{
    /// <summary>Time without activity after which an unlocked page locks.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    private bool _unlocked;
    private DateTimeOffset _lastActivity;

    /// <summary>Raised when the stored state changes between locked and unlocked.</summary>
    public event EventHandler? Changed;

    /// <summary>True when the page is locked, including when the idle time has run out.</summary>
    public bool IsLocked => !_unlocked || IsIdleExpired();

    /// <summary>Unlocks the page and starts the idle timer.</summary>
    public void Unlock()
    {
        var changed = !_unlocked;
        _unlocked = true;
        _lastActivity = time.GetUtcNow();
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Locks the page.</summary>
    public void Lock()
    {
        if (!_unlocked)
        {
            return;
        }

        _unlocked = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restarts the idle timer. If the idle time already ran out, the page locks instead.</summary>
    public void RecordActivity()
    {
        if (!_unlocked)
        {
            return;
        }

        if (IsIdleExpired())
        {
            Lock();
            return;
        }

        _lastActivity = time.GetUtcNow();
    }

    /// <summary>Locks the page when the idle time has run out; call periodically.</summary>
    public void CheckIdle()
    {
        if (_unlocked && IsIdleExpired())
        {
            Lock();
        }
    }

    private bool IsIdleExpired() => time.GetUtcNow() - _lastActivity >= IdleTimeout;
}
