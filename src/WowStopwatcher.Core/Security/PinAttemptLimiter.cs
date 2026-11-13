namespace WowStopwatcher.Core.Security;

/// <summary>
/// Limits wrong PIN guesses: after <see cref="MaxTries"/> consecutive wrong PINs, attempts are refused for <see cref="LockoutDuration"/>.
/// When the lockout ends a fresh set of tries starts. A correct PIN resets the count.
/// State is in memory only, so it resets when the app restarts.
/// </summary>
public sealed class PinAttemptLimiter(TimeProvider time)
{
    /// <summary>Consecutive wrong PINs allowed before the lockout.</summary>
    public const int MaxTries = 5;

    /// <summary>How long attempts are refused after <see cref="MaxTries"/> wrong PINs.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    private int _failures;
    private DateTimeOffset? _lockedUntil;

    /// <summary>True while attempts are refused.</summary>
    public bool IsLockedOut => RemainingLockout > TimeSpan.Zero;

    /// <summary>Time left in the lockout, or zero when attempts are allowed.</summary>
    public TimeSpan RemainingLockout
    {
        get
        {
            Refresh();
            return _lockedUntil is { } until ? until - time.GetUtcNow() : TimeSpan.Zero;
        }
    }

    /// <summary>Wrong PINs still allowed before the lockout (<see cref="MaxTries"/> when none were made; zero while locked out).</summary>
    public int RemainingTries
    {
        get
        {
            Refresh();
            return _lockedUntil is null ? MaxTries - _failures : 0;
        }
    }

    /// <summary>Records a wrong PIN. Ignored while locked out. Starts the lockout on the <see cref="MaxTries"/>th.</summary>
    public void RecordFailure()
    {
        Refresh();
        if (_lockedUntil is not null)
        {
            return;
        }

        _failures++;
        if (_failures >= MaxTries)
        {
            _lockedUntil = time.GetUtcNow() + LockoutDuration;
        }
    }

    /// <summary>Records a correct PIN: clears the count.</summary>
    public void RecordSuccess()
    {
        _failures = 0;
        _lockedUntil = null;
    }

    private void Refresh()
    {
        if (_lockedUntil is { } until && time.GetUtcNow() >= until)
        {
            _lockedUntil = null;
            _failures = 0;
        }
    }
}
