using System.Security.Cryptography;

namespace WowStopwatcher.Core.Security;

/// <summary>
/// The 256-bit random key that encrypts the database. It is never typed by the user: it is kept in an OS secret store and,
/// wrapped by a recovery code (<see cref="DatabaseKeyWrapper"/>), next to the database.
/// </summary>
public sealed class DatabaseKey
{
    /// <summary>Key length in bytes.</summary>
    public const int Length = 32;

    private readonly byte[] _bytes;

    private DatabaseKey(byte[] bytes) => _bytes = bytes;

    /// <summary>Creates a new key from the system's cryptographic random number generator.</summary>
    public static DatabaseKey Generate() => new(RandomNumberGenerator.GetBytes(Length));

    /// <summary>Wraps existing key bytes. The array is copied. Throws when it is not <see cref="Length"/> bytes.</summary>
    public static DatabaseKey FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A database key is {Length} bytes.", nameof(bytes));
        }

        return new DatabaseKey(bytes.ToArray());
    }

    /// <summary>Parses a key written by <see cref="ToHex"/>. Returns null when the text is not 64 hex digits.</summary>
    public static DatabaseKey? FromHex(string? hex)
    {
        if (hex is null || hex.Length != Length * 2)
        {
            return null;
        }

        try
        {
            return new DatabaseKey(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A copy of the key bytes.</summary>
    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>The key as 64 lowercase hex digits (the form handed to SQLite).</summary>
    public string ToHex() => Convert.ToHexString(_bytes).ToLowerInvariant();

    /// <summary>Compares two keys in constant time.</summary>
    public bool KeyEquals(DatabaseKey? other) => other is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
}
