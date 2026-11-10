using System.Globalization;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core.Tests.Localization;

public class LanguageResolverTests
{
    private static readonly string[] Available = ["en", "uk"];

    [Theory]
    [InlineData("uk", "en-US", "uk")]
    [InlineData("en", "uk-UA", "en")]
    [InlineData(null, "uk-UA", "uk")]
    [InlineData("system", "uk-UA", "uk")]
    [InlineData("system", "de-DE", "en")]
    [InlineData("fr", "uk-UA", "uk")]
    [InlineData("fr", "de-DE", "en")]
    [InlineData("UK", "en-US", "uk")]
    [InlineData("", "uk-UA", "uk")]
    public void Resolves(string? stored, string systemCulture, string expected)
    {
        Assert.Equal(expected, LanguageResolver.Resolve(Available, stored, CultureInfo.GetCultureInfo(systemCulture)));
    }
}
