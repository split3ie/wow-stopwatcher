namespace WowStopwatcher.Core;

/// <summary>
/// Build-time switches for security features (the Security group of <c>Features.props</c>).
/// </summary>
/// <param name="Encryption">Off: the database is opened plain exactly as before encryption existed (no key store, no
/// "Protect your data" or recovery code windows); an encrypted database is refused untouched, backups are plain <c>.zip</c> files.</param>
public sealed record SecurityCapabilities(bool Encryption)
{
    /// <summary>All capabilities on.</summary>
    public static SecurityCapabilities All { get; } = new(true);
}
