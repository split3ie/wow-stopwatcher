using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core.Tests.Localization;

public class TranslationTests
{
    private static Translation English() => Translation.FromJson("en", """
        {
          "_meta": { "name": "English", "englishName": "English" },
          "a": "Hello",
          "b": "Hi {name}, {count} left",
          "braces": "Use {{name}} like {name}",
          "onlyEn": "Only English",
          "items": { "one": "{count} item", "other": "{count} items" }
        }
        """);

    private static Translation Ukrainian(Translation fallback) => Translation.FromJson("uk", """
        {
          "_meta": { "name": "Українська", "englishName": "Ukrainian" },
          "a": "Привіт",
          "items": { "one": "{count} елемент", "few": "{count} елементи", "many": "{count} елементів", "other": "{count} елемента" }
        }
        """, fallback);

    [Fact]
    public void Reads_meta_and_culture()
    {
        var uk = Ukrainian(English());
        Assert.Equal("uk", uk.Code);
        Assert.Equal("Українська", uk.NativeName);
        Assert.Equal("Ukrainian", uk.EnglishName);
        Assert.Equal("uk", uk.Culture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void Returns_own_text()
    {
        Assert.Equal("Привіт", Ukrainian(English()).Get("a"));
    }

    [Fact]
    public void Falls_back_to_english_for_missing_key()
    {
        Assert.Equal("Only English", Ukrainian(English()).Get("onlyEn"));
    }

    [Fact]
    public void Missing_everywhere_returns_bracketed_key()
    {
        Assert.Equal("[nope.key]", Ukrainian(English()).Get("nope.key"));
        Assert.Equal("[nope.key]", Ukrainian(English()).GetPlural("nope.key", 3));
    }

    [Fact]
    public void Fills_named_placeholders_with_invariant_numbers()
    {
        var text = English().Get("b", ("name", "Ann"), ("count", 1234.5));
        Assert.Equal("Hi Ann, 1234.5 left", text);
    }

    [Fact]
    public void Leaves_unknown_placeholder_as_written()
    {
        Assert.Equal("Hi {name}, 3 left", English().Get("b", ("count", 3)));
    }

    [Fact]
    public void Doubled_braces_are_literal()
    {
        Assert.Equal("Use {name} like Bob", English().Get("braces", ("name", "Bob")));
    }

    [Theory]
    [InlineData(1, "1 item")]
    [InlineData(0, "0 items")]
    [InlineData(2, "2 items")]
    public void English_plural(long count, string expected)
    {
        Assert.Equal(expected, English().GetPlural("items", count));
    }

    [Theory]
    [InlineData(1, "1 елемент")]
    [InlineData(2, "2 елементи")]
    [InlineData(5, "5 елементів")]
    [InlineData(21, "21 елемент")]
    [InlineData(112, "112 елементів")]
    public void Ukrainian_plural(long count, string expected)
    {
        Assert.Equal(expected, Ukrainian(English()).GetPlural("items", count));
    }

    [Fact]
    public void Plural_falls_back_to_other_when_category_missing()
    {
        var t = Translation.FromJson("uk", """{ "x": { "other": "{count} шт." } }""");
        Assert.Equal("1 шт.", t.GetPlural("x", 1));
    }

    [Fact]
    public void Plural_supplies_count_and_other_args()
    {
        var t = Translation.FromJson("en", """{ "x": { "one": "{count} {what}", "other": "{count} {what}s" } }""");
        Assert.Equal("2 cats", t.GetPlural("x", 2, ("what", "cat")));
    }

    [Fact]
    public void Invalid_json_throws_format_exception_naming_the_file()
    {
        var ex = Assert.Throws<FormatException>(() => Translation.FromJson("xx", "{ not json"));
        Assert.Contains("xx.json", ex.Message);
    }

    [Fact]
    public void Wrong_value_type_throws_format_exception()
    {
        var ex = Assert.Throws<FormatException>(() => Translation.FromJson("xx", """{ "k": 5 }"""));
        Assert.Contains("'k'", ex.Message);
    }

    [Fact]
    public void Loc_defaults_to_english_and_loads_embedded_files()
    {
        Assert.Contains(Loc.Available, t => t.Code == "en");
        Assert.Contains(Loc.Available, t => t.Code == "uk");
        Assert.Equal("[no.such.key]", Loc.T("no.such.key"));
    }
}
