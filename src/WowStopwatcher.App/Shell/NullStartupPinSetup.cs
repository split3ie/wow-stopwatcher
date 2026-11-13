namespace WowStopwatcher.App.Shell;

/// <summary>Used when no feature supports a PIN: there is no PIN step and nothing to apply.</summary>
public sealed class NullStartupPinSetup : IStartupPinSetup
{
    public void Apply(string? pin)
    {
    }
}
