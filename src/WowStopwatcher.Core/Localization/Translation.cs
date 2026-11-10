using System.Globalization;
using System.Text.Json;

namespace WowStopwatcher.Core.Localization;

/// <summary>
/// One loaded language: its texts and plural texts by key. Pure (no static state), so it can be built and tested directly.
/// A key missing here is looked up in <see cref="Fallback"/> (English), and if that fails too the key itself is returned
/// in brackets, for example <c>[today.stopAll]</c>, so a gap is visible but never crashes.
/// </summary>
public sealed class Translation
{
    private readonly IReadOnlyDictionary<string, string> _texts;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _plurals;

    public Translation(
        string code,
        string nativeName,
        string englishName,
        IReadOnlyDictionary<string, string> texts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? plurals = null,
        Translation? fallback = null)
    {
        Code = code;
        NativeName = nativeName;
        EnglishName = englishName;
        _texts = texts;
        _plurals = plurals ?? new Dictionary<string, IReadOnlyDictionary<string, string>>();
        Fallback = fallback;
        Culture = FindCulture(code);
    }

    /// <summary>Language code, the file name without extension (for example <c>uk</c>).</summary>
    public string Code { get; }

    /// <summary>Name of the language in itself (for example "Українська").</summary>
    public string NativeName { get; }

    public string EnglishName { get; }

    /// <summary>Culture for day and month names; the invariant culture when the code is not a known culture.</summary>
    public CultureInfo Culture { get; }

    /// <summary>The translation used for keys this one lacks, or null.</summary>
    public Translation? Fallback { get; }

    /// <summary>The text for <paramref name="key"/>; <c>{name}</c> placeholders are filled from <paramref name="args"/>.</summary>
    public string Get(string key, params (string Name, object? Value)[] args)
    {
        for (var t = this; t is not null; t = t.Fallback)
        {
            if (t._texts.TryGetValue(key, out var text))
            {
                return TemplateFormatter.Format(text, args);
            }
        }

        return Missing(key);
    }

    /// <summary>
    /// The text for <paramref name="key"/> in the plural form of <paramref name="count"/>; <c>{count}</c> is supplied
    /// and the other placeholders come from <paramref name="args"/>. A missing category falls back to <c>other</c>.
    /// </summary>
    public string GetPlural(string key, long count, params (string Name, object? Value)[] args)
    {
        var all = new (string Name, object? Value)[args.Length + 1];
        all[0] = ("count", count);
        args.CopyTo(all, 1);

        for (var t = this; t is not null; t = t.Fallback)
        {
            if (t._plurals.TryGetValue(key, out var forms))
            {
                var category = PluralRules.GetCategory(t.Code, count);
                if (forms.TryGetValue(category, out var form) || forms.TryGetValue("other", out form))
                {
                    return TemplateFormatter.Format(form, all);
                }
            }
            else if (t._texts.TryGetValue(key, out var text))
            {
                return TemplateFormatter.Format(text, all);
            }
        }

        return Missing(key);
    }

    /// <summary>Parses a translation file; <paramref name="code"/> is the file name without extension.</summary>
    /// <exception cref="FormatException">The file is not valid or a value has the wrong type.</exception>
    public static Translation FromJson(string code, string json, Translation? fallback = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var plurals = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            var nativeName = code;
            var englishName = code;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name == "_meta")
                {
                    nativeName = ReadMeta(property.Value, "name") ?? code;
                    englishName = ReadMeta(property.Value, "englishName") ?? code;
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    texts[property.Name] = property.Value.GetString()!;
                }
                else if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var form in property.Value.EnumerateObject())
                    {
                        forms[form.Name] = form.Value.ValueKind == JsonValueKind.String
                            ? form.Value.GetString()!
                            : throw new FormatException($"{code}.json: plural form '{property.Name}.{form.Name}' must be a string.");
                    }

                    plurals[property.Name] = forms;
                }
                else
                {
                    throw new FormatException($"{code}.json: '{property.Name}' must be a string or an object of plural forms.");
                }
            }

            return new Translation(code, nativeName, englishName, texts, plurals, fallback);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{code}.json is not valid JSON: {ex.Message}", ex);
        }
    }

    private static string? ReadMeta(JsonElement meta, string name) =>
        meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Missing(string key) => $"[{key}]";

    private static CultureInfo FindCulture(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
