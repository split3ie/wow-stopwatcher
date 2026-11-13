using System.Security.Cryptography;
using System.Text;

namespace WowStopwatcher.Core.Security;

/// <summary>
/// A recovery code the user writes down: 24 characters of a Crockford base32 alphabet (120 bits of entropy), shown in six groups
/// of four separated by dashes, for example <c>7K3M-X9QD-2VBH-R4TN-A8YW-C5ZE</c>. Input is case-insensitive, ignores spaces and dashes,
/// and reads the look-alikes O, I and L as 0, 1 and 1.
/// </summary>
public static class RecoveryCode
{
    /// <summary>Crockford base32: digits and letters without I, L, O and U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Number of significant characters (5 bits each).</summary>
    public const int Length = 24;

    private const int GroupSize = 4;

    /// <summary>Creates a new random code in its display form (groups separated by dashes).</summary>
    public static string Generate()
    {
        var builder = new StringBuilder(Length + Length / GroupSize - 1);
        for (var i = 0; i < Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                builder.Append('-');
            }

            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The canonical form of typed input: upper case, without spaces and dashes, O read as 0 and I or L read as 1.
    /// Other characters are kept, so <see cref="IsValid"/> can reject them.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var raw in input)
        {
            if (raw == '-' || char.IsWhiteSpace(raw))
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw);
            builder.Append(c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            });
        }

        return builder.ToString();
    }

    /// <summary>True when the input, once normalized, is exactly <see cref="Length"/> characters of <see cref="Alphabet"/>.</summary>
    public static bool IsValid(string? input)
    {
        var normalized = Normalize(input);
        return normalized.Length == Length && normalized.All(c => Alphabet.Contains(c));
    }
}
