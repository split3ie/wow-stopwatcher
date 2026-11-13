using WowStopwatcher.Core;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>
/// The PIN that locks the Invoices page: stored as a hash in settings, checked with a limit on wrong tries, and the locked or unlocked state.
/// The PIN counts as active only when it is enabled and a valid hash exists.
/// </summary>
public sealed class InvoicesPinService
{
    public const string HashKey = "invoicing_pin_hash";
    public const string EnabledKey = "invoicing_pin_enabled";

    private readonly ISettingsRepository _settings;
    private readonly int _hashIterations;

    /// <param name="hashIterations">PBKDF2 iterations for new hashes; tests lower it, the app uses the default.</param>
    public InvoicesPinService(ISettingsRepository settings, TimeProvider time, int hashIterations = PinHash.DefaultIterations)
    {
        _settings = settings;
        _hashIterations = hashIterations;
        Limiter = new PinAttemptLimiter(time);
        LockState = new PinLockState(time);
    }

    /// <summary>Raised when the PIN is set, enabled or disabled.</summary>
    public event EventHandler? Changed;

    /// <summary>Counts wrong PINs; shared by every place that asks for the PIN.</summary>
    public PinAttemptLimiter Limiter { get; }

    /// <summary>Locked or unlocked state of the Invoices page.</summary>
    public PinLockState LockState { get; }

    /// <summary>True when a PIN is enabled and a valid hash is stored.</summary>
    public bool IsEnabled => _settings.Get(EnabledKey) == "true" && HasPin;

    /// <summary>True when a valid hash is stored.</summary>
    public bool HasPin => _settings.Get(HashKey) is { } hash && hash.StartsWith(PinHash.Prefix + "$", StringComparison.Ordinal);

    /// <summary>Checks <paramref name="pin"/> against the stored hash off the UI thread, counting wrong tries.</summary>
    public async Task<PinCheckResult> VerifyAsync(string pin)
    {
        if (Limiter.IsLockedOut)
        {
            return LockedOut();
        }

        var stored = _settings.Get(HashKey);
        var ok = await Task.Run(() => PinHash.Verify(pin, stored));
        if (ok)
        {
            Limiter.RecordSuccess();
            return new PinCheckResult(PinCheckStatus.Success, PinAttemptLimiter.MaxTries, TimeSpan.Zero);
        }

        Limiter.RecordFailure();
        return Limiter.IsLockedOut ? LockedOut() : new PinCheckResult(PinCheckStatus.Wrong, Limiter.RemainingTries, TimeSpan.Zero);
    }

    /// <summary>The current lockout as a result.</summary>
    public PinCheckResult LockedOut() => new(PinCheckStatus.LockedOut, 0, Limiter.RemainingLockout);

    /// <summary>Stores a hash of <paramref name="pin"/> and enables the PIN. Throws when it is not 6 to 8 digits.</summary>
    public void Set(string pin) => Store(PinHash.Create(Validated(pin), _hashIterations));

    /// <summary>Like <see cref="Set"/> but hashes off the UI thread.</summary>
    public async Task SetAsync(string pin)
    {
        var valid = Validated(pin);
        var hash = await Task.Run(() => PinHash.Create(valid, _hashIterations));
        Store(hash);
    }

    /// <summary>Turns the PIN off, clears the stored hash and locks the page state. Turning it on again needs a new PIN.</summary>
    public void Disable()
    {
        _settings.Set(EnabledKey, "false");
        _settings.Set(HashKey, string.Empty);
        LockState.Lock();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the wrong-PIN count (after a reset with the recovery code).</summary>
    public void ResetAttempts() => Limiter.RecordSuccess();

    private void Store(string hash)
    {
        _settings.Set(HashKey, hash);
        _settings.Set(EnabledKey, "true");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Validated(string pin) =>
        PinPolicy.IsValid(pin) ? pin : throw new ArgumentException("A PIN is 6 to 8 digits.", nameof(pin));
}
