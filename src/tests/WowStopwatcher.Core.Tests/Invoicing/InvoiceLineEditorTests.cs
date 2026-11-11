using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceLineEditorTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly InvoiceLineSource A = new("a", "Alpha");
    private static readonly InvoiceLineSource B = new("b", "Beta");

    private static InvoiceLine L(int minutes, InvoiceLineSource source, string note = "") => new(Day, minutes, note, [source]);

    private static List<InvoiceLine> Sample() => [L(60, A, "x"), L(30, B, "y"), L(15, A, "x")];

    [Fact]
    public void Split_ReplacesLineWithTwoParts()
    {
        var result = InvoiceLineEditor.Split(Sample(), 1, 10);

        Assert.Equal([60, 10, 20, 15], result.Select(l => l.Minutes));
        Assert.Equal("y", result[2].Note);
        Assert.Equal("b", result[2].Sources[0].StopwatchId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(30)]
    [InlineData(31)]
    public void Split_InvalidFirstPart_Throws(int first)
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Split(Sample(), 1, first));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Split_IndexOutOfRange_Throws(int index)
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Split(Sample(), index, 5));
    }

    [Fact]
    public void Split_DoesNotChangeInput()
    {
        var input = Sample();
        InvoiceLineEditor.Split(input, 0, 10);

        Assert.Equal(3, input.Count);
        Assert.Equal(60, input[0].Minutes);
    }

    [Fact]
    public void Merge_SumsMinutes_UnitesSourcesWithoutDuplicates_JoinsDistinctNotes()
    {
        var result = InvoiceLineEditor.Merge(Sample(), [2, 0]);

        Assert.Equal(2, result.Count);
        var merged = result[0];
        Assert.Equal(75, merged.Minutes);
        Assert.Equal("x", merged.Note);
        Assert.Equal(["a"], merged.Sources.Select(s => s.StopwatchId));
        Assert.False(merged.IsMerged);
        Assert.Equal(30, result[1].Minutes);
    }

    [Fact]
    public void Merge_DifferentStopwatchesAndNotes_PutsMergedAtFirstPosition()
    {
        var result = InvoiceLineEditor.Merge(Sample(), [1, 0]);

        Assert.Equal(2, result.Count);
        Assert.Equal(90, result[0].Minutes);
        Assert.Equal("x; y", result[0].Note);
        Assert.Equal(["a", "b"], result[0].Sources.Select(s => s.StopwatchId));
        Assert.True(result[0].IsMerged);
        Assert.Equal(15, result[1].Minutes);
    }

    [Fact]
    public void Merge_SkipsEmptyNotes()
    {
        var lines = new List<InvoiceLine> { L(10, A), L(10, B, "only") };

        Assert.Equal("only", InvoiceLineEditor.Merge(lines, [0, 1])[0].Note);
    }

    [Fact]
    public void Merge_FewerThanTwoIndices_Throws()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Merge(Sample(), [1]));
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Merge(Sample(), []));
    }

    [Fact]
    public void Merge_DuplicateIndices_Throws()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Merge(Sample(), [1, 1]));
    }

    [Fact]
    public void Merge_IndexOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Merge(Sample(), [0, 5]));
    }

    [Fact]
    public void Adjust_SetsMinutes()
    {
        Assert.Equal([60, 45, 15], InvoiceLineEditor.Adjust(Sample(), 1, 45).Select(l => l.Minutes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Adjust_NonPositive_Throws(int minutes)
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Adjust(Sample(), 0, minutes));
    }

    [Fact]
    public void Adjust_IndexOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Adjust(Sample(), 9, 5));
    }

    [Fact]
    public void SetNote_Trims()
    {
        var result = InvoiceLineEditor.SetNote(Sample(), 0, "  hello  ");

        Assert.Equal("hello", result[0].Note);
        Assert.Equal("y", result[1].Note);
    }

    [Fact]
    public void SetNote_Null_BecomesEmpty()
    {
        Assert.Equal(string.Empty, InvoiceLineEditor.SetNote(Sample(), 0, null)[0].Note);
    }

    [Fact]
    public void SetNote_IndexOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.SetNote(Sample(), -1, "n"));
    }

    [Fact]
    public void AddCustom_AppendsLineWithoutSources()
    {
        var result = InvoiceLineEditor.AddCustom(Sample(), Day, 25, " note ");

        var added = result[^1];
        Assert.Equal(4, result.Count);
        Assert.True(added.IsCustom);
        Assert.False(added.IsMerged);
        Assert.Equal(25, added.Minutes);
        Assert.Equal("note", added.Note);
        Assert.Equal(Day, added.Date);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AddCustom_NonPositive_Throws(int minutes) =>
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.AddCustom(Sample(), Day, minutes, ""));

    [Fact]
    public void DeleteCustom_RemovesOnlyCustomLines()
    {
        var lines = InvoiceLineEditor.AddCustom(Sample(), Day, 25, "");

        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.DeleteCustom(lines, 0));
        Assert.Equal(Sample().Count, InvoiceLineEditor.DeleteCustom(lines, 3).Count);
    }

    [Fact]
    public void Merge_WithCustomLine_Throws()
    {
        var lines = InvoiceLineEditor.AddCustom(Sample(), Day, 25, "");

        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.Merge(lines, [0, 3]));
    }

    [Fact]
    public void Split_CustomLine_KeepsItCustom()
    {
        var lines = InvoiceLineEditor.AddCustom([], Day, 40, "n");

        var result = InvoiceLineEditor.Split(lines, 0, 15);

        Assert.Equal([15, 25], result.Select(l => l.Minutes));
        Assert.All(result, l => Assert.True(l.IsCustom));
    }
}
