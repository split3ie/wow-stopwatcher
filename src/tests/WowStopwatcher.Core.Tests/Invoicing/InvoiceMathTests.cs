using System.Globalization;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceMathTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(60, "1.00")]
    [InlineData(90, "1.50")]
    [InlineData(20, "0.33")]
    [InlineData(40, "0.67")]
    [InlineData(1, "0.02")]
    public void Hours_RoundedToTwoDecimals(int minutes, string expected)
    {
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), InvoiceMath.Hours(minutes));
    }

    [Theory]
    [InlineData(60, "25", "25.00")]
    [InlineData(90, "25", "37.50")]
    [InlineData(20, "10", "3.33")]
    [InlineData(1, "0.5", "0.01")]
    [InlineData(3, "0.5", "0.03")]
    [InlineData(0, "100", "0.00")]
    public void Amount_RoundedAwayFromZero(int minutes, string rate, string expected)
    {
        Assert.Equal(
            decimal.Parse(expected, CultureInfo.InvariantCulture),
            InvoiceMath.Amount(minutes, decimal.Parse(rate, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Amount_MidpointRoundsUp()
    {
        // 1 minute at 1.50/h = 0.025 -> 0.03
        Assert.Equal(0.03m, InvoiceMath.Amount(1, 1.5m));
    }
}
