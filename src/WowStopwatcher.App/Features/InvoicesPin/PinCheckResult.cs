namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>What <see cref="InvoicesPinService.VerifyAsync"/> found out.</summary>
/// <param name="Status">Right, wrong or refused because of the lockout.</param>
/// <param name="RemainingTries">Wrong PINs still allowed (zero while locked out).</param>
/// <param name="RemainingLockout">Time left in the lockout (zero when not locked out).</param>
public sealed record PinCheckResult(PinCheckStatus Status, int RemainingTries, TimeSpan RemainingLockout);
