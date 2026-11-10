using System.Text.Json;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core.Tests.Localization;

/// <summary>Checks every embedded translation file, so a new language file is validated by CI without any test change.</summary>
public class TranslationFilesTests
{
    private static readonly Dictionary<string, Dictionary<string, JsonElement>> Files = Load();

    public static TheoryData<string> LanguageCodes()
    {
        var data = new TheoryData<string>();
        foreach (var code in Files.Keys)
        {
            data.Add(code);
        }

        return data;
    }

    private static Dictionary<string, Dictionary<string, JsonElement>> Load()
    {
        var result = new Dictionary<string, Dictionary<string, JsonElement>>();
        foreach (var (code, json) in TranslationLoader.ReadFiles())
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                result[code] = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch (JsonException)
            {
                // Reported by the valid-JSON test below.
                result[code] = [];
            }
        }

        return result;
    }

    private static Dictionary<string, JsonElement> English => Files["en"];

    private static IEnumerable<KeyValuePair<string, JsonElement>> Entries(Dictionary<string, JsonElement> file) =>
        file.Where(p => p.Key != "_meta");

    private static IReadOnlySet<string> Placeholders(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? TemplateFormatter.PlaceholderNames(value.GetString()!)
            : value.EnumerateObject().SelectMany(f => TemplateFormatter.PlaceholderNames(f.Value.GetString() ?? "")).ToHashSet();

    [Fact]
    public void English_file_exists()
    {
        Assert.True(Files.ContainsKey("en"), "en.json is missing: it is the base and fallback language.");
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void File_is_valid_json_with_meta(string code)
    {
        var (_, json) = TranslationLoader.ReadFiles().Single(f => f.Code == code);
        var ex = Record.Exception(() => JsonDocument.Parse(json).Dispose());
        Assert.True(ex is null, $"{code}.json is not valid JSON: {ex?.Message}");

        var file = Files[code];
        Assert.True(file.TryGetValue("_meta", out var meta) && meta.ValueKind == JsonValueKind.Object, $"{code}.json: '_meta' object is missing.");
        foreach (var field in new[] { "name", "englishName" })
        {
            Assert.True(
                meta.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()),
                $"{code}.json: '_meta.{field}' is missing or empty.");
        }
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void Values_are_strings_or_plural_objects(string code)
    {
        foreach (var (key, value) in Entries(Files[code]))
        {
            Assert.True(
                value.ValueKind is JsonValueKind.String or JsonValueKind.Object,
                $"{code}.json: '{key}' must be a string or an object of plural forms.");
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var form in value.EnumerateObject())
                {
                    Assert.True(form.Value.ValueKind == JsonValueKind.String, $"{code}.json: '{key}.{form.Name}' must be a string.");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void Has_no_keys_missing_from_english(string code)
    {
        var unknown = Entries(Files[code]).Select(p => p.Key).Where(k => !English.ContainsKey(k)).ToList();
        Assert.True(unknown.Count == 0, $"{code}.json has keys that are not in en.json (typo?): {string.Join(", ", unknown)}");
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void Placeholders_match_english(string code)
    {
        foreach (var (key, value) in Entries(Files[code]).Where(p => English.ContainsKey(p.Key)))
        {
            if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object))
            {
                continue;
            }

            var expected = Placeholders(English[key]).Order().ToList();
            // English says "{ordinal}" ("1st"), which does not exist in other languages; they use "{number}" instead.
            var actual = Placeholders(value).Select(p => p == "number" && expected.Contains("ordinal") ? "ordinal" : p).Order().ToList();
            Assert.True(
                expected.SequenceEqual(actual),
                $"{code}.json: '{key}' must use the placeholders {{{string.Join("}, {", expected)}}} like en.json, but uses {{{string.Join("}, {", actual)}}}.");
        }
    }

    [Theory]
    [MemberData(nameof(LanguageCodes))]
    public void Plural_keys_match_english_and_have_every_needed_category(string code)
    {
        foreach (var (key, value) in Entries(Files[code]).Where(p => English.ContainsKey(p.Key)))
        {
            var englishIsPlural = English[key].ValueKind == JsonValueKind.Object;
            var isPlural = value.ValueKind == JsonValueKind.Object;
            Assert.True(
                englishIsPlural == isPlural,
                $"{code}.json: '{key}' must be {(englishIsPlural ? "a plural object" : "a plain string")} like in en.json.");

            if (!isPlural)
            {
                continue;
            }

            var names = value.EnumerateObject().Select(p => p.Name).ToList();
            var unknown = names.Where(n => !PluralRules.AllCategories.Contains(n)).ToList();
            Assert.True(
                unknown.Count == 0,
                $"{code}.json: '{key}' has unknown plural categories: {string.Join(", ", unknown)}. Allowed: {string.Join(", ", PluralRules.AllCategories)}.");

            var missing = PluralRules.RequiredCategories(code).Where(c => !names.Contains(c)).ToList();
            Assert.True(
                missing.Count == 0,
                $"{code}.json: '{key}' is missing plural categories: {string.Join(", ", missing)}.");
        }
    }
}
