using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoicePeriodTests
{
    [Fact]
    public void Days_AndKey()
    {
        var february = new InvoicePeriod(2028, 2);

        Assert.Equal(new DateOnly(2028, 2, 1), february.FirstDay);
        Assert.Equal(new DateOnly(2028, 2, 29), february.LastDay);
        Assert.Equal("2028-02", february.Key);
        Assert.Equal("2028-02", february.ToString());
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    public void InvalidValues_Throw(int year, int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InvoicePeriod(year, month));
    }

    [Fact]
    public void Contains_OnlyDaysOfThatMonth()
    {
        var period = new InvoicePeriod(2026, 11);

        Assert.True(period.Contains(new DateOnly(2026, 11, 1)));
        Assert.True(period.Contains(new DateOnly(2026, 11, 30)));
        Assert.False(period.Contains(new DateOnly(2026, 10, 31)));
        Assert.False(period.Contains(new DateOnly(2026, 12, 1)));
        Assert.False(period.Contains(new DateOnly(2025, 11, 5)));
    }

    [Fact]
    public void From_UsesYearAndMonth()
    {
        Assert.Equal(new InvoicePeriod(2026, 11), InvoicePeriod.From(new DateOnly(2026, 11, 17)));
    }

    [Fact]
    public void RangeUtc_IsHalfOpenInTheZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("p2", TimeSpan.FromHours(2), "p2", "p2");

        var (start, end) = new InvoicePeriod(2026, 11).RangeUtc(zone);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 22, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new InvoicePeriod(2026, 1), new InvoicePeriod(2026, 1));
        Assert.NotEqual(new InvoicePeriod(2026, 1), new InvoicePeriod(2026, 2));
    }
}
