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
    public void Day_reports_billed_and_unallocated_minutes()
    {
        var day = Day(Line(60, "work"), Line(30, string.Empty));

        Assert.Equal(60, day.BilledMinutes);
        Assert.Equal(30, day.UnallocatedMinutes);
    }
}
