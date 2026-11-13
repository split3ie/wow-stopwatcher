namespace WowStopwatcher.Core.Security;

/// <summary>A <see cref="DatabaseKey"/> encrypted with a key derived from a recovery code, with everything needed to unwrap it.</summary>
/// <param name="Kdf">Name of the key derivation function (<see cref="DatabaseKeyWrapper.KdfName"/>).</param>
/// <param name="Iterations">Number of KDF iterations.</param>
/// <param name="Salt">KDF salt.</param>
/// <param name="Nonce">AES-GCM nonce.</param>
/// <param name="Ciphertext">The encrypted key (same length as the key).</param>
/// <param name="Tag">AES-GCM authentication tag.</param>
public sealed record WrappedDatabaseKey(string Kdf, int Iterations, byte[] Salt, byte[] Nonce, byte[] Ciphertext, byte[] Tag);
