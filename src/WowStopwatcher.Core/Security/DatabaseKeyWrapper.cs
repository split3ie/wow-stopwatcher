using System.Security.Cryptography;
using System.Text;

namespace WowStopwatcher.Core.Security;

/// <summary>
/// Wraps a <see cref="DatabaseKey"/> with a key derived from a <see cref="RecoveryCode"/>: PBKDF2-SHA256 (random 16-byte salt)
/// into an AES-256-GCM key (random 12-byte nonce, 16-byte tag).
/// </summary>
public static class DatabaseKeyWrapper
{
    /// <summary>Name stored in <see cref="WrappedDatabaseKey.Kdf"/>.</summary>
    public const string KdfName = "PBKDF2-SHA256";

    /// <summary>Iterations used for new wrapped keys.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Largest iteration count accepted when unwrapping, so a tampered file cannot make the app hang.</summary>
    public const int MaxIterations = 10_000_000;

    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private static readonly byte[] AssociatedData = "WowStopwatcher.DatabaseKey.v1"u8.ToArray();

    /// <summary>Encrypts <paramref name="key"/> with the recovery code. <paramref name="iterations"/> is for tests; use the default otherwise.</summary>
    public static WrappedDatabaseKey Wrap(DatabaseKey key, string recoveryCode, int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plain = key.ToBytes();
        var ciphertext = new byte[plain.Length];
        var tag = new byte[TagLength];

        var wrapKey = Derive(recoveryCode, salt, iterations);
        try
        {
            using var aes = new AesGcm(wrapKey, TagLength);
            aes.Encrypt(nonce, plain, ciphertext, tag, AssociatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(plain);
        }

        return new WrappedDatabaseKey(KdfName, iterations, salt, nonce, ciphertext, tag);
    }

    /// <summary>Returns the key, or null when the code is wrong or the wrapped data is damaged or unsupported.</summary>
    public static DatabaseKey? TryUnwrap(WrappedDatabaseKey wrapped, string recoveryCode)
    {
        if (wrapped.Kdf != KdfName
            || wrapped.Iterations is < 1 or > MaxIterations
            || wrapped.Salt.Length != SaltLength
            || wrapped.Nonce.Length != NonceLength
            || wrapped.Tag.Length != TagLength
            || wrapped.Ciphertext.Length != DatabaseKey.Length)
        {
            return null;
        }

        var wrapKey = Derive(recoveryCode, wrapped.Salt, wrapped.Iterations);
        var plain = new byte[DatabaseKey.Length];
        try
        {
            using var aes = new AesGcm(wrapKey, TagLength);
            aes.Decrypt(wrapped.Nonce, wrapped.Ciphertext, wrapped.Tag, plain, AssociatedData);
            return DatabaseKey.FromBytes(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Derive(string recoveryCode, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(RecoveryCode.Normalize(recoveryCode)), salt, iterations, HashAlgorithmName.SHA256, 32);
}
