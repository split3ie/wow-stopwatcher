using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>Fingerprint of everything that goes into a generated file, to detect changes after generation.</summary>
public static class InvoiceInputHash
{
    /// <summary>
    /// Lowercase SHA-256 hex of a canonical string of the lines (in order), placeholder values (by name), rate, currency and template hash.
    /// Formatting is invariant; the same input always gives the same hash.
    /// </summary>
    public static string Compute(
        IReadOnlyList<InvoiceLine> lines,
        IReadOnlyDictionary<string, string?> values,
        decimal rate,
        string currency,
        string? templateSha256)
    {
        var sb = new StringBuilder();
        Field(sb, "invoice-input-v1");
        Field(sb, rate.ToString("0.############################", CultureInfo.InvariantCulture));
        Field(sb, currency);
        Field(sb, templateSha256);

        Field(sb, lines.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var line in lines)
        {
            Field(sb, line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Field(sb, line.Minutes.ToString(CultureInfo.InvariantCulture));
            Field(sb, line.Note);
            Field(sb, line.Sources.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var source in line.Sources)
            {
                Field(sb, source.StopwatchId);
                Field(sb, source.StopwatchName);
            }
        }

        Field(sb, values.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (key, value) in values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Field(sb, key);
            Field(sb, value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    // Length-prefixed so no value can imitate a separator; null differs from empty.
    private static void Field(StringBuilder sb, string? value)
    {
        if (value is null)
        {
            sb.Append("~;");
        }
        else
        {
            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        }
    }
}
