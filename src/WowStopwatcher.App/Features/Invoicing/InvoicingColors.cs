using Avalonia.Media;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The color choices of a company and the tab colors derived from a company color.</summary>
internal static class InvoicingColors
{
    /// <summary>The same 16 presets as the stopwatch colors: English name (the suffix of the <c>settings.color.*</c> text) and hex.</summary>
    public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
    [
        ("Blue", "#2F6FEB"),
        ("Green", "#1F9D55"),
        ("Orange", "#C96A0C"),
        ("Purple", "#7C4DDB"),
        ("Pink", "#D6407A"),
        ("Teal", "#0E8C8C"),
        ("Red", "#D92D20"),
        ("Amber", "#B8860B"),
        ("Lime", "#5C9E1B"),
        ("Cyan", "#0891B2"),
        ("Indigo", "#4F46E5"),
        ("Slate", "#475569"),
        ("Brown", "#8B5A2B"),
        ("Magenta", "#B5179E"),
        ("Olive", "#6B7A1F"),
        ("Navy", "#1E3A8A"),
    ];

    /// <summary>The color of <paramref name="hex"/>, or the first preset when it cannot be read.</summary>
    public static Color Parse(string? hex) =>
        hex is not null && Color.TryParse(hex, out var color) ? color : Color.Parse(Palette[0].Hex);

    /// <summary>The color mixed with white: the background of a selected tab.</summary>
    public static Color Tint(Color color) => Mix(color, Colors.White, 0.88);

    /// <summary>The color darkened: the text of a selected tab.</summary>
    public static Color Ink(Color color) => Mix(color, Colors.Black, 0.35);

    /// <summary>The first preset no company uses yet; when all are used, the presets repeat in order.</summary>
    public static string NextFree(IReadOnlyCollection<string> used)
    {
        foreach (var (_, hex) in Palette)
        {
            if (!used.Contains(hex, StringComparer.OrdinalIgnoreCase))
            {
                return hex;
            }
        }

        return Palette[used.Count % Palette.Count].Hex;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
}
