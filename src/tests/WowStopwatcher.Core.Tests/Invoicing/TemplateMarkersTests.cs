using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class TemplateMarkersTests
{
    [Fact]
    public void Find_ReturnsDistinctNamesInOrder()
    {
        Assert.Equal(["b", "a.c", "spaced_1"], TemplateMarkers.Find("{{b}} x {{a.c}} {{b}} {{ spaced_1 }}"));
    }

    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{a-b}}")]
    [InlineData("{a}")]
    [InlineData("{{ a b }}")]
    [InlineData("no markers")]
    public void Find_IgnoresInvalidMarkers(string text)
    {
        Assert.Empty(TemplateMarkers.Find(text));
    }

    [Fact]
    public void Replace_ReplacesKnown_LeavesUnknownUntouched()
    {
        var result = TemplateMarkers.Replace("A={{a}}; B={{ b }}; C={{c}}", n => n switch { "a" => "1", "b" => "2", _ => null });

        Assert.Equal("A=1; B=2; C={{c}}", result);
    }

    [Fact]
    public void Replace_ValueContainingMarker_IsNotReplacedAgain()
    {
        Assert.Equal("{{b}}", TemplateMarkers.Replace("{{a}}", n => n == "a" ? "{{b}}" : "x"));
    }
}
