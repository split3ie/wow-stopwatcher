namespace WowStopwatcher.Core.Localization;

/// <summary>
/// CLDR plural rules for integers, by language code. A language that is not listed uses the English-like rule
/// (<c>one</c> for 1, otherwise <c>other</c>). To support a language with other rules, add it to
/// <see cref="GetCategory"/> and <see cref="RequiredCategories"/>; the consistency tests then check its translation file.
/// Rules: https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html
/// </summary>
public static class PluralRules
{
    /// <summary>Every category name a translation file may use.</summary>
    public static IReadOnlyList<string> AllCategories { get; } = ["zero", "one", "two", "few", "many", "other"];

    private static readonly string[] EnglishLike = ["one", "other"];
    private static readonly string[] EastSlavic = ["one", "few", "many", "other"];

    /// <summary>The plural category of <paramref name="count"/> in <paramref name="languageCode"/>.</summary>
    public static string GetCategory(string languageCode, long count)
    {
        var n = Math.Abs(count);
        if (IsEastSlavic(languageCode))
        {
            var mod10 = n % 10;
            var mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11)
            {
                return "one";
            }

            if (mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14))
            {
                return "few";
            }

            return "many";
        }

        return n == 1 ? "one" : "other";
    }

    /// <summary>The categories a plural value must define in <paramref name="languageCode"/>.</summary>
    public static IReadOnlyList<string> RequiredCategories(string languageCode) =>
        IsEastSlavic(languageCode) ? EastSlavic : EnglishLike;

    private static bool IsEastSlavic(string languageCode) =>
        languageCode.ToLowerInvariant() is "uk" or "ru" or "be";
}
