namespace WowStopwatcher.Core.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(3723, "01:02:03")]
    [InlineData(360000, "100:00:00")]
    [InlineData(-5, "00:00:00")]
    public void Clock(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Clock_WithMilliseconds() =>
        Assert.Equal("00:01:02.045", TimeFormat.Clock(new TimeSpan(0, 0, 1, 2, 45), withMs: true));

    [Theory]
    [InlineData(3723, "1h 2m 3s")]
    [InlineData(123, "2m 3s")]
    [InlineData(3, "3s")]
    [InlineData(0, "0s")]
    public void Human(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Human(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(5400, "1h 30m")]
    [InlineData(3600, "1h")]
    [InlineData(1800, "30m")]
    [InlineData(3629, "1h")]
    [InlineData(3630, "1h 1m")]
    public void HoursMinutes(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.HoursMinutes(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData("05:00:00", true, 18000)]
    [InlineData("0:45:00", true, 2700)]
    [InlineData("00:00:00", false, 0)]
    [InlineData("5:00", false, 0)]
    [InlineData("aa:00:00", false, 0)]
    [InlineData("01:60:00", false, 0)]
    [InlineData("", false, 0)]
    public void TryParseGoal(string text, bool ok, int seconds)
    {
        Assert.Equal(ok, TimeFormat.TryParseGoal(text, out var goal));
        Assert.Equal(TimeSpan.FromSeconds(seconds), goal);
    }

    [Fact]
    public void FormatGoal() => Assert.Equal("05:00:00", TimeFormat.FormatGoal(TimeSpan.FromHours(5)));

    [Theory]
    [InlineData(18000, "5h")]
    [InlineData(2700, "45m")]
    [InlineData(5400, "1h 30m")]
    public void GoalLabel(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.GoalLabel(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    public void Ordinal(int n, string expected) => Assert.Equal(expected, TimeFormat.Ordinal(n));
}
