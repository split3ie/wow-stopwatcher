namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>Checks a typed recovery code against the open database.</summary>
public interface IRecoveryCodeVerifier
{
    /// <summary>True when <paramref name="code"/> is the recovery code of the open database.</summary>
    Task<bool> VerifyAsync(string code);
}
