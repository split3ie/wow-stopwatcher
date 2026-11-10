using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core.Tests.Localization;

public class PluralRulesTests
{
    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "other")]
    [InlineData(0, "other")]
    public void English_like(long n, string expected) => Assert.Equal(expected, PluralRules.GetCategory("en", n));

    [Fact]
    public void Unknown_language_uses_english_like_rule()
    {
        Assert.Equal("one", PluralRules.GetCategory("xx", 1));
        Assert.Equal("other", PluralRules.GetCategory("xx", 5));
        Assert.Equal(["one", "other"], PluralRules.RequiredCategories("xx"));
    }

    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(5, "many")]
    [InlineData(11, "many")]
    [InlineData(12, "many")]
    [InlineData(21, "one")]
    [InlineData(22, "few")]
    [InlineData(25, "many")]
    [InlineData(111, "many")]
    [InlineData(0, "many")]
    public void East_slavic(long n, string expected)
    {
        Assert.Equal(expected, PluralRules.GetCategory("uk", n));
        Assert.Equal(expected, PluralRules.GetCategory("ru", n));
        Assert.Equal(expected, PluralRules.GetCategory("be", n));
    }

    [Fact]
    public void East_slavic_requires_four_categories()
    {
        Assert.Equal(["one", "few", "many", "other"], PluralRules.RequiredCategories("uk"));
    }
}
