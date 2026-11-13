using System.Globalization;
using System.Security.Cryptography;

namespace WowStopwatcher.Core.Security;

/// <summary>
/// Hashes and verifies the Invoices PIN with PBKDF2-SHA256 (random 16-byte salt). The result is one string:
/// <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>.
/// </summary>
public static class PinHash
{
    /// <summary>Algorithm prefix of the stored string.</summary>
    public const string Prefix = "pbkdf2-sha256";

    /// <summary>Iterations used for new hashes.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Largest iteration count accepted when verifying, so a tampered value cannot make the app hang.</summary>
    public const int MaxIterations = 10_000_000;

    private const int SaltLength = 16;
    private const int HashLength = 32;

    /// <summary>Hashes <paramref name="pin"/>. <paramref name="iterations"/> is for tests; use the default otherwise.</summary>
    public static string Create(string pin, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(pin, salt, iterations);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>True when <paramref name="pin"/> matches <paramref name="stored"/>. A malformed or unsupported stored value gives false.</summary>
    public static bool Verify(string? pin, string? stored)
    {
        if (pin is null || stored is null)
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4
            || parts[0] != Prefix
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations is < 1 or > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != SaltLength || expected.Length != HashLength)
        {
            return false;
        }

        var actual = Derive(pin, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, HashLength);
}
