using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceBillingTests
{
    private static InvoiceLine Line(int minutes, string note) =>
        new(new DateOnly(2026, 11, 2), minutes, note, [new InvoiceLineSource("a", "English")]);

    private static InvoiceDay Day(params InvoiceLine[] lines) =>
        new(new DateOnly(2026, 11, 2), false, lines, lines.Sum(l => l.Minutes), false);

    [Fact]
    public void Only_lines_with_a_note_are_billed()
    {
        var days = new[] { Day(Line(60, "work"), Line(30, "")), Day(Line(15, "  "), Line(10, "x")) };

        Assert.Equal([60, 10], InvoiceBilling.BilledLines(days).Select(l => l.Minutes).ToArray());
        Assert.Equal(70, InvoiceBilling.BilledMinutes(days));
        Assert.Equal(45, InvoiceBilling.UnallocatedMinutes(days));
    }

    [Fact]
    public void Invoiced_minutes_are_the_reported_time_or_else_the_billed_lines()
    {
        var days = new[] { Day(Line(60, "work"), Line(30, "")) };

        Assert.Equal(60, InvoiceBilling.InvoicedMinutes(days, null));
        Assert.Equal(600, InvoiceBilling.InvoicedMinutes(days, 600));
        Assert.Equal(90, InvoiceBilling.InvoicedMinutes([Day(Line(30, ""))], 90));
        Assert.Equal(0, InvoiceBilling.InvoicedMinutes([Day(Line(30, ""))], null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1, true)]
    [InlineData(59999, true)]
    [InlineData(60000, false)]
    public void Reported_minutes_are_valid_from_1_to_999_59(int minutes, bool valid) =>
        Assert.Equal(valid, InvoiceBilling.IsValidReportedMinutes(minutes));

    [Fact]
    public void Day_reports_billed_and_unallocated_minutes()
    {
        var day = Day(Line(60, "work"), Line(30, string.Empty));

        Assert.Equal(60, day.BilledMinutes);
        Assert.Equal(30, day.UnallocatedMinutes);
    }
}
