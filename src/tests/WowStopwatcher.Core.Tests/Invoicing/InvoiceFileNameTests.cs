using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceFileNameTests
{
    private static string Render(string pattern, params (string Key, string Value)[] values) =>
        InvoiceFileName.Render(pattern, n => values.Where(v => v.Key == n).Select(v => v.Value).FirstOrDefault());

    [Fact]
    public void ReplacesMarkers_AndAppendsPdf()
    {
        Assert.Equal(
            "Invoice 2026-11 Acme.pdf",
            Render("Invoice {{period.month}} {{company.name}}", ("period.month", "2026-11"), ("company.name", "Acme")));
    }

    [Theory]
    [InlineData("a.PDF", "a.PDF")]
    [InlineData("a.pdf", "a.pdf")]
    [InlineData("a.pdf.", "a.pdf")]
    public void ExistingPdfExtension_IsNotDoubled(string pattern, string expected)
    {
        Assert.Equal(expected, Render(pattern));
    }

    [Fact]
    public void InvalidCharacters_BecomeUnderscore()
    {
        Assert.Equal("a_b_c_d_e_f_g_h_i.pdf", Render("a<b>c:d\"e/f\\g|h?i"));
        Assert.Equal("x_y.pdf", Render("x*y"));
        Assert.Equal("a_b.pdf", Render("a\tb"));
    }

    [Fact]
    public void ValueWithPathCharacters_IsSanitized()
    {
        Assert.Equal("A_B.pdf", Render("{{n}}", ("n", "A/B")));
    }

    [Fact]
    public void SpacesAndDots_AreTrimmed()
    {
        Assert.Equal("name.pdf", Render("  ..name.. "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Empty_BecomesInvoice(string pattern)
    {
        Assert.Equal("invoice.pdf", Render(pattern));
    }

    [Theory]
    [InlineData("CON", "_CON.pdf")]
    [InlineData("con", "_con.pdf")]
    [InlineData("NUL.txt", "_NUL.txt.pdf")]
    [InlineData("COM1", "_COM1.pdf")]
    [InlineData("lpt9", "_lpt9.pdf")]
    [InlineData("COM0", "COM0.pdf")]
    [InlineData("CONSOLE", "CONSOLE.pdf")]
    [InlineData("CON.pdf", "_CON.pdf")]
    public void ReservedDeviceNames_GetPrefix(string pattern, string expected)
    {
        Assert.Equal(expected, Render(pattern));
    }

    [Fact]
    public void UnknownMarker_StaysInName()
    {
        Assert.Equal("{{x}}.pdf", Render("{{x}}"));
    }
}
