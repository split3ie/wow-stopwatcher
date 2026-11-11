using System.Globalization;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceBuiltinsTests
{
    private static readonly CultureInfo Us = new("en-US");
    private static readonly CultureInfo De = new("de-DE");
    private static readonly InvoicePeriod Period = new(2026, 2);

    [Fact]
    public void Create_FormatsValues()
    {
        var v = InvoiceBuiltins.Create(100, 25m, "EUR", "Acme", Period, 7, new DateOnly(2026, 3, 5), Us);

        Assert.Equal("1.67", v[InvoiceBuiltins.Hours]);
        Assert.Equal("100", v[InvoiceBuiltins.Minutes]);
        Assert.Equal("25.00", v[InvoiceBuiltins.Rate]);
        Assert.Equal("41.67", v[InvoiceBuiltins.Amount]);
        Assert.Equal("EUR", v[InvoiceBuiltins.Currency]);
        Assert.Equal("2026-02", v[InvoiceBuiltins.PeriodMonth]);
        Assert.Equal("February 2026", v[InvoiceBuiltins.PeriodName]);
        Assert.Equal("2/1/2026", v[InvoiceBuiltins.PeriodStart]);
        Assert.Equal("2/28/2026", v[InvoiceBuiltins.PeriodEnd]);
        Assert.Equal("7", v[InvoiceBuiltins.InvoiceNumber]);
        Assert.Equal("3/5/2026", v[InvoiceBuiltins.Today]);
        Assert.Equal("Acme", v[InvoiceBuiltins.CompanyName]);
    }

    [Fact]
    public void Create_NumbersStayInvariant_EvenInCommaCulture_DatesFollowCulture()
    {
        var v = InvoiceBuiltins.Create(90, 10m, "EUR", "Acme", Period, null, new DateOnly(2026, 3, 5), De);

        Assert.Equal("1.50", v[InvoiceBuiltins.Hours]);
        Assert.Equal("15.00", v[InvoiceBuiltins.Amount]);
        Assert.Equal("February 2026", v[InvoiceBuiltins.PeriodName]);
        Assert.Equal("01.02.2026", v[InvoiceBuiltins.PeriodStart]);
        Assert.Equal(string.Empty, v[InvoiceBuiltins.InvoiceNumber]);
    }

    [Fact]
    public void Create_CustomDateFormat_AppliesToDates()
    {
        var v = InvoiceBuiltins.Create(0, 0m, "USD", "", Period, null, new DateOnly(2026, 3, 5), Us, "dd.MM.yyyy");

        Assert.Equal("01.02.2026", v[InvoiceBuiltins.PeriodStart]);
        Assert.Equal("28.02.2026", v[InvoiceBuiltins.PeriodEnd]);
        Assert.Equal("05.03.2026", v[InvoiceBuiltins.Today]);
    }

    [Fact]
    public void Create_CustomDateFormat_KeepsSlashAsTyped()
    {
        var uk = new CultureInfo("uk-UA");
        var v = InvoiceBuiltins.Create(0, 0m, "USD", "", Period, null, new DateOnly(2026, 3, 5), uk, "dd/MM/yyyy");

        Assert.Equal("01/02/2026", v[InvoiceBuiltins.PeriodStart]);
        Assert.Equal("05/03/2026", v[InvoiceBuiltins.Today]);
    }

    [Fact]
    public void Create_EmptyDateFormat_UsesCultureShortDateWithCultureSeparator()
    {
        var v = InvoiceBuiltins.Create(0, 0m, "USD", "", Period, null, new DateOnly(2026, 3, 5), new CultureInfo("uk-UA"), "");

        Assert.Equal("05.03.2026", v[InvoiceBuiltins.Today]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_EmptyDateFormat_UsesCultureShortDate(string? format)
    {
        var v = InvoiceBuiltins.Create(0, 0m, "USD", "", Period, null, new DateOnly(2026, 3, 5), Us, format);

        Assert.Equal("3/5/2026", v[InvoiceBuiltins.Today]);
    }

    [Fact]
    public void CreateLine_CustomDateFormat_AppliesToLineDate()
    {
        var line = new InvoiceLine(new DateOnly(2026, 2, 9), 5, "", []);

        Assert.Equal("09.02.2026", InvoiceBuiltins.CreateLine(line, 1m, Us, "dd.MM.yyyy")[InvoiceBuiltins.LineDate]);
        Assert.Equal("2/9/2026", InvoiceBuiltins.CreateLine(line, 1m, Us, null)[InvoiceBuiltins.LineDate]);
    }

    [Fact]
    public void Create_PeriodYear_IsFourInvariantDigits()
    {
        var values = InvoiceBuiltins.Create(0, 0m, "USD", "", new InvoicePeriod(2026, 11), null, new DateOnly(2026, 12, 1), new CultureInfo("uk-UA"));

        Assert.Equal("2026", values[InvoiceBuiltins.PeriodYear]);
    }

    [Fact]
    public void Create_PeriodMonthName_IsAlwaysEnglish()
    {
        var period = new InvoicePeriod(2026, 11);
        var today = new DateOnly(2026, 12, 1);

        Assert.Equal("November", InvoiceBuiltins.Create(0, 0m, "USD", "", period, null, today, Us)[InvoiceBuiltins.PeriodMonthName]);
        Assert.Equal("November",InvoiceBuiltins.Create(0, 0m, "USD", "", period, null, today, new CultureInfo("uk-UA"))[InvoiceBuiltins.PeriodMonthName]);
    }

    [Fact]
    public void Create_ContainsEveryNonLineBuiltin()
    {
        var v = InvoiceBuiltins.Create(0, 0m, "USD", "", Period, null, new DateOnly(2026, 3, 5), Us);

        Assert.Equal(
            InvoiceBuiltins.All.Where(n => !InvoiceBuiltins.IsLineMarker(n)).Order(),
            v.Keys.Order());
    }

    [Fact]
    public void CreateLine_FormatsValues()
    {
        var line = new InvoiceLine(
            new DateOnly(2026, 2, 9), 75, "note",
            [new InvoiceLineSource("a", "Alpha"), new InvoiceLineSource("b", "Beta")]);

        var v = InvoiceBuiltins.CreateLine(line, 40m, Us);

        Assert.Equal("2/9/2026", v[InvoiceBuiltins.LineDate]);
        Assert.Equal("Alpha, Beta", v[InvoiceBuiltins.LineStopwatch]);
        Assert.Equal("1:15", v[InvoiceBuiltins.LineTime]);
        Assert.Equal("1.25", v[InvoiceBuiltins.LineHours]);
        Assert.Equal("50.00", v[InvoiceBuiltins.LineAmount]);
        Assert.Equal("note", v[InvoiceBuiltins.LineNote]);
        Assert.Equal(InvoiceBuiltins.All.Where(InvoiceBuiltins.IsLineMarker).Order(), v.Keys.Order());
    }

    [Fact]
    public void CreateLine_CustomLine_HasEmptyStopwatch()
    {
        var line = new InvoiceLine(new DateOnly(2026, 2, 9), 30, "n", []);

        Assert.Equal(string.Empty, InvoiceBuiltins.CreateLine(line, 40m, Us)[InvoiceBuiltins.LineStopwatch]);
    }

    [Fact]
    public void CreateLine_TimeUnderOneHour_PadsMinutes()
    {
        var line = new InvoiceLine(new DateOnly(2026, 2, 9), 5, "", [new InvoiceLineSource("a", "A")]);

        Assert.Equal("0:05", InvoiceBuiltins.CreateLine(line, 1m, Us)[InvoiceBuiltins.LineTime]);
    }

    [Fact]
    public void IsBuiltin_AndIsLineMarker()
    {
        Assert.True(InvoiceBuiltins.IsBuiltin("hours"));
        Assert.True(InvoiceBuiltins.IsBuiltin("line.note"));
        Assert.False(InvoiceBuiltins.IsBuiltin("custom"));
        Assert.False(InvoiceBuiltins.IsBuiltin("Hours"));
        Assert.True(InvoiceBuiltins.IsLineMarker("line.anything"));
        Assert.False(InvoiceBuiltins.IsLineMarker("hours"));
    }
}
