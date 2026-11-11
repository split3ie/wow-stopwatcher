using System.Text.RegularExpressions;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceInputHashTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);

    private static List<InvoiceLine> Lines() =>
    [
        new(Day, 60, "n", [new InvoiceLineSource("a", "Alpha")]),
        new(Day, 30, "", [new InvoiceLineSource("b", "Beta")]),
    ];

    private static Dictionary<string, string?> Values() => new() { ["x"] = "1", ["y"] = null };

    private static string Hash(
        IReadOnlyList<InvoiceLine>? lines = null,
        IReadOnlyDictionary<string, string?>? values = null,
        decimal rate = 25m,
        string currency = "EUR",
        string? template = "abc") =>
        InvoiceInputHash.Compute(lines ?? Lines(), values ?? Values(), rate, currency, template);

    [Fact]
    public void IsLowercaseSha256Hex_AndStable()
    {
        var hash = Hash();

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);
        Assert.Equal(hash, Hash());
    }

    [Fact]
    public void ValueOrder_DoesNotMatter()
    {
        var reordered = new Dictionary<string, string?> { ["y"] = null, ["x"] = "1" };

        Assert.Equal(Hash(), Hash(values: reordered));
    }

    [Fact]
    public void RateFormatting_DoesNotMatter()
    {
        Assert.Equal(Hash(rate: 25m), Hash(rate: 25.00m));
    }

    [Fact]
    public void LineOrder_Matters()
    {
        var reversed = Lines();
        reversed.Reverse();

        Assert.NotEqual(Hash(), Hash(lines: reversed));
    }

    [Fact]
    public void AnyInputChange_ChangesTheHash()
    {
        var baseline = Hash();

        Assert.NotEqual(baseline, Hash(rate: 26m));
        Assert.NotEqual(baseline, Hash(currency: "USD"));
        Assert.NotEqual(baseline, Hash(template: "abd"));
        Assert.NotEqual(baseline, Hash(template: null));
        Assert.NotEqual(baseline, Hash(values: new Dictionary<string, string?> { ["x"] = "2", ["y"] = null }));
        Assert.NotEqual(baseline, Hash(values: new Dictionary<string, string?> { ["x"] = "1", ["y"] = "" }));
        Assert.NotEqual(baseline, Hash(lines: InvoiceLineEditor.Adjust(Lines(), 0, 61)));
        Assert.NotEqual(baseline, Hash(lines: InvoiceLineEditor.SetNote(Lines(), 1, "z")));
    }

    [Fact]
    public void FieldBoundaries_AreUnambiguous()
    {
        var one = new Dictionary<string, string?> { ["ab"] = "c" };
        var two = new Dictionary<string, string?> { ["a"] = "bc" };

        Assert.NotEqual(Hash(values: one), Hash(values: two));
    }
}
