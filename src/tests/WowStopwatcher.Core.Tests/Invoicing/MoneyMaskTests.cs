using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class MoneyMaskTests
{
    [Fact]
    public void Hidden_ReturnsMask()
    {
        Assert.Equal("****", MoneyMask.Apply("12.50", true));
        Assert.Equal(MoneyMask.Mask, MoneyMask.Apply("", true));
    }

    [Fact]
    public void Shown_ReturnsValue()
    {
        Assert.Equal("12.50", MoneyMask.Apply("12.50", false));
    }
}
