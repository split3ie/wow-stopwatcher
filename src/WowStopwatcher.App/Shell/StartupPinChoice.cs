namespace WowStopwatcher.App.Shell;

/// <summary>What the user chose in the PIN step. <see cref="Pin"/> is null for "Don't use a PIN".</summary>
public sealed record StartupPinChoice(string? Pin);
