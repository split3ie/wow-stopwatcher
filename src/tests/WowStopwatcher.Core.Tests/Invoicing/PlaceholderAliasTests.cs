using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class PlaceholderAliasTests
{
    [Theory]
    [InlineData("total")]
    [InlineData("client.address_2")]
    [InlineData("A1")]
    public void Valid_ReturnsNull(string alias)
    {
        Assert.Null(PlaceholderAlias.Validate(alias));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty(string? alias)
    {
        Assert.Equal(PlaceholderAlias.Empty, PlaceholderAlias.Validate(alias));
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("_x")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData(".a")]
    [InlineData("ключ")]
    public void InvalidCharacters(string alias)
    {
        Assert.Equal(PlaceholderAlias.InvalidCharacters, PlaceholderAlias.Validate(alias));
    }

    [Theory]
    [InlineData("line.custom")]
    [InlineData("LINE.date")]
    [InlineData("line.date")]
    public void Reserved(string alias)
    {
        Assert.Equal(PlaceholderAlias.Reserved, PlaceholderAlias.Validate(alias));
    }

    [Theory]
    [InlineData("hours")]
    [InlineData("amount")]
    [InlineData("period.name")]
    [InlineData("company.name")]
    [InlineData("Hours")]
    public void BuiltInNames_AreAllowed(string alias)
    {
        Assert.Null(PlaceholderAlias.Validate(alias));
    }
}
