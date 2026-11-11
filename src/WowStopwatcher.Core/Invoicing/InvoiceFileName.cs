using System.Text;

namespace WowStopwatcher.Core.Invoicing;

/// <summary>Builds a safe PDF file name from a pattern with <c>{{name}}</c> markers.</summary>
public static class InvoiceFileName
{
    private const string Extension = ".pdf";
    private const char Replacement = '_';
    private const string InvalidCharacters = "<>:\"/\\|?*";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Replaces markers, then makes the result valid on every OS: characters invalid on Windows become '_', spaces and dots at the
    /// ends are trimmed, a reserved device name gets a '_' prefix, an empty name becomes "invoice", and ".pdf" is appended unless present.
    /// </summary>
    public static string Render(string pattern, Func<string, string?> resolve)
    {
        var text = TemplateMarkers.Replace(pattern, resolve);

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(char.IsControl(c) || InvalidCharacters.Contains(c) ? Replacement : c);
        }

        var name = Trim(builder.ToString());
        var extension = Extension;
        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            extension = name[^Extension.Length..];
            name = Trim(name[..^Extension.Length]);
        }

        if (name.Length == 0)
        {
            name = "invoice";
        }

        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        if (ReservedNames.Contains(stem.TrimEnd(' ')))
        {
            name = Replacement + name;
        }

        return name + extension;
    }

    private static string Trim(string value) => value.Trim(' ', '.');
}
