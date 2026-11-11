using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceTimeTextTests
{
    [Theory]
    [InlineData("8", 480)]
    [InlineData("8:00", 480)]
    [InlineData("0:45", 45)]
    [InlineData(" 1:5 ", 65)]
    [InlineData("12:59", 779)]
    public void Parses_hours_and_minutes(string text, int expected)
    {
        Assert.True(InvoiceTimeText.TryParse(text, out var minutes));
        Assert.Equal(expected, minutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("8:60")]
    [InlineData("8:")]
    [InlineData(":30")]
    [InlineData("1:2:3")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData("1000")]
    public void Rejects_other_text(string? text)
    {
        Assert.False(InvoiceTimeText.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(3, "0:03")]
    [InlineData(483, "8:03")]
    [InlineData(6000, "100:00")]
    public void Formats_as_h_mm(int minutes, string expected)
    {
        Assert.Equal(expected, InvoiceTimeText.Format(minutes));
    }
}
