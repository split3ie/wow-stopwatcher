namespace WowStopwatcher.Core.Localization;

/// <summary>
/// Finds the translation files embedded in this assembly. Every <c>Localization/Translations/*.json</c> is embedded with
/// the logical name <c>WowStopwatcher.Translations.&lt;code&gt;.json</c>, so a new language needs only a new file.
/// </summary>
public static class TranslationLoader
{
    private const string Prefix = "WowStopwatcher.Translations.";
    private const string Suffix = ".json";

    /// <summary>Every embedded translation file as (language code, JSON text), ordered by code.</summary>
    public static IReadOnlyList<(string Code, string Json)> ReadFiles()
    {
        var assembly = typeof(TranslationLoader).Assembly;
        var files = new List<(string Code, string Json)>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var code = name[Prefix.Length..^Suffix.Length];
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files.Add((code, reader.ReadToEnd()));
        }

        return files.OrderBy(f => f.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>All embedded languages, English first. Every other language falls back to English.</summary>
    public static IReadOnlyList<Translation> LoadAll()
    {
        var files = ReadFiles();
        var english = files.Where(f => f.Code == LanguageResolver.DefaultCode)
            .Select(f => Translation.FromJson(f.Code, f.Json)).FirstOrDefault();

        var result = new List<Translation>();
        if (english is not null)
        {
            result.Add(english);
        }

        foreach (var (code, json) in files.Where(f => f.Code != LanguageResolver.DefaultCode))
        {
            result.Add(Translation.FromJson(code, json, english));
        }

        return result;
    }
}
