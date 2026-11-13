using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace WowStopwatcher.Core.Security;

/// <summary>
/// Password-protected container for a backup. Layout (all in one byte array):
/// <c>"WSBK"</c> (4) | format version (1) | PBKDF2 iterations (4, big endian) | salt (16) | AES-GCM nonce (12) | ciphertext | tag (16).
/// The key is PBKDF2-SHA256 of the UTF-8 password; AES-256-GCM encrypts the payload and authenticates the whole header
/// (everything before the ciphertext) as associated data. Everything is held in memory, so it suits backups of up to a few hundred MB.
/// </summary>
public static class BackupEnvelope
{
    /// <summary>Format version written by <see cref="Encrypt"/>.</summary>
    public const byte CurrentVersion = 1;

    /// <summary>Iterations used for new envelopes.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Largest iteration count accepted when decrypting, so a tampered file cannot make the app hang.</summary>
    public const int MaxIterations = 10_000_000;

    /// <summary>Fewest characters in the password of a new backup. Opening an existing backup accepts any password.</summary>
    public const int MinPasswordLength = 10;

    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int HeaderLength = 4 + 1 + 4 + SaltLength + NonceLength;

    private static readonly byte[] Magic = "WSBK"u8.ToArray();

    /// <summary>True when <paramref name="data"/> starts with the envelope magic bytes (the start of a file is enough).</summary>
    public static bool HasMagic(ReadOnlySpan<byte> data) => data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    /// <summary>True when <paramref name="password"/> is long enough for a new backup (<see cref="MinPasswordLength"/>).</summary>
    public static bool IsAcceptablePassword(string? password) => password is { Length: >= MinPasswordLength };

    /// <summary>Encrypts <paramref name="plain"/>. <paramref name="iterations"/> is for tests; use the default otherwise.</summary>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static byte[] Encrypt(ReadOnlySpan<byte> plain, string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        var result = new byte[HeaderLength + plain.Length + TagLength];
        Magic.CopyTo(result, 0);
        result[4] = CurrentVersion;
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(5, 4), iterations);
        RandomNumberGenerator.Fill(result.AsSpan(9, SaltLength + NonceLength));

        var key = Derive(password, result.AsSpan(9, SaltLength), iterations);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(
                result.AsSpan(9 + SaltLength, NonceLength),
                plain,
                result.AsSpan(HeaderLength, plain.Length),
                result.AsSpan(HeaderLength + plain.Length, TagLength),
                result.AsSpan(0, HeaderLength));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return result;
    }

    /// <summary>Decrypts an envelope. <paramref name="plain"/> is null unless the status is <see cref="BackupDecryptStatus.Success"/>.</summary>
    public static BackupDecryptStatus TryDecrypt(ReadOnlySpan<byte> data, string password, out byte[]? plain)
    {
        plain = null;
        if (data.Length < HeaderLength + TagLength || !HasMagic(data) || data[4] != CurrentVersion)
        {
            return BackupDecryptStatus.Damaged;
        }

        var iterations = BinaryPrimitives.ReadInt32BigEndian(data.Slice(5, 4));
        if (iterations is < 1 or > MaxIterations)
        {
            return BackupDecryptStatus.Damaged;
        }

        if (string.IsNullOrEmpty(password))
        {
            return BackupDecryptStatus.WrongPasswordOrTampered;
        }

        var cipherLength = data.Length - HeaderLength - TagLength;
        var buffer = new byte[cipherLength];
        var key = Derive(password, data.Slice(9, SaltLength), iterations);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(
                data.Slice(9 + SaltLength, NonceLength),
                data.Slice(HeaderLength, cipherLength),
                data.Slice(HeaderLength + cipherLength, TagLength),
                buffer,
                data[..HeaderLength]);
            plain = buffer;
            return BackupDecryptStatus.Success;
        }
        catch (CryptographicException)
        {
            return BackupDecryptStatus.WrongPasswordOrTampered;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Derive(string password, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
}
