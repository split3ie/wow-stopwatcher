using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class DayStartTextTests
{
    [Theory]
    [InlineData("00:00", 0)]
    [InlineData("20:00", 1200)]
    [InlineData("8:30", 510)]
    [InlineData("23:59", 1439)]
    public void TryParse_AcceptsTimes(string text, int minutes)
    {
        Assert.True(DayStartText.TryParse(text, out var parsed));
        Assert.Equal(minutes, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("abc")]
    public void TryParse_RejectsOthers(string text) => Assert.False(DayStartText.TryParse(text, out _));

    [Fact]
    public void Format_PadsHoursAndMinutes() => Assert.Equal("08:05", DayStartText.Format(485));
}
