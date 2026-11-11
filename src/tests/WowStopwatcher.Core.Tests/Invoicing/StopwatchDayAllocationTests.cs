using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class StopwatchDayAllocationTests
{
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly InvoiceLineSource A = new("a", "Alpha");
    private static readonly InvoiceLineSource B = new("b", "Beta");

    private static InvoiceLine L(int minutes, string note, params InvoiceLineSource[] sources) => new(Day, minutes, note, sources);

    // Stopwatch A: entries 60 "x" and 30 "y"; two un-noted lines whose minutes are ignored.
    private static List<InvoiceLine> Sample() =>
    [
        L(45, "other", B),
        L(60, "x", A),
        L(25, "merged", A, B),
        L(10, "", A),
        L(30, "y", A),
        L(20, "", A),
    ];

    private static StopwatchDayAllocation Alloc(int tracked = 120) => StopwatchDayAllocation.From(Sample(), "a", "Alpha", tracked);

    [Fact]
    public void From_TakesEntriesAndMergedAndDerivesUnallocated()
    {
        var a = Alloc(115);

        Assert.Equal([(60, "x"), (30, "y")], a.Entries.Select(e => (e.Minutes, e.Note)));
        Assert.Equal(90, a.BilledMinutes);
        Assert.Equal(25, a.Unallocated);
        Assert.Equal(0, a.OverMinutes);
        Assert.Equal(-25, a.DifferenceMinutes);
        Assert.Single(a.MergedLines);
        Assert.Equal(25, a.MergedLines[0].Minutes);
    }

    [Fact]
    public void From_NoLines_AllTrackedIsUnallocated()
    {
        var a = StopwatchDayAllocation.From([L(45, "other", B)], "a", "Alpha", 50);

        Assert.Empty(a.Entries);
        Assert.Equal(0, a.BilledMinutes);
        Assert.Equal(50, a.Unallocated);
        Assert.Equal(-50, a.DifferenceMinutes);
    }

    [Fact]
    public void Allocate_8h03Tracked_8h_LeavesThreeMinutesUnallocated()
    {
        var a = StopwatchDayAllocation.From([], "a", "Alpha", 483).Allocate(480, "  work ");

        Assert.Equal(new StopwatchDayAllocation.Entry(480, "work"), a.Entries[0]);
        Assert.Equal(480, a.BilledMinutes);
        Assert.Equal(3, a.Unallocated);
        Assert.Equal(0, a.OverMinutes);
        Assert.Equal(-3, a.DifferenceMinutes);
    }

    [Fact]
    public void Allocate_7h58Tracked_8h_IsOverByTwoMinutes()
    {
        var a = StopwatchDayAllocation.From([], "a", "Alpha", 478).Allocate(480, "work");

        Assert.Equal(480, a.BilledMinutes);
        Assert.Equal(0, a.Unallocated);
        Assert.Equal(2, a.OverMinutes);
        Assert.Equal(2, a.DifferenceMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_NonPositiveMinutes_Throws(int minutes)
    {
        Assert.Throws<ArgumentException>(() => Alloc().Allocate(minutes, "z"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Allocate_EmptyNote_Throws(string? note)
    {
        Assert.Throws<ArgumentException>(() => Alloc().Allocate(5, note));
    }

    [Fact]
    public void Edit_ChangesEntryAndRecomputesUnallocated()
    {
        var a = Alloc().Edit(0, 20, "x2");

        Assert.Equal(new StopwatchDayAllocation.Entry(20, "x2"), a.Entries[0]);
        Assert.Equal(50, a.BilledMinutes);
        Assert.Equal(70, a.Unallocated);
    }

    [Fact]
    public void Edit_AboveTracked_IsAllowed()
    {
        var a = Alloc().Edit(0, 200, "x");

        Assert.Equal(0, a.Unallocated);
        Assert.Equal(110, a.OverMinutes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, -3)]
    [InlineData(2, 5)]
    [InlineData(-1, 5)]
    public void Edit_Invalid_Throws(int index, int minutes)
    {
        Assert.Throws<ArgumentException>(() => Alloc().Edit(index, minutes, "n"));
    }

    [Fact]
    public void Edit_EmptyNote_Throws()
    {
        Assert.Throws<ArgumentException>(() => Alloc().Edit(0, 10, " "));
    }

    [Fact]
    public void Delete_RemovesEntryAndIncreasesUnallocated()
    {
        var a = Alloc().Delete(0);

        Assert.Equal([(30, "y")], a.Entries.Select(e => (e.Minutes, e.Note)));
        Assert.Equal(30, a.BilledMinutes);
        Assert.Equal(90, a.Unallocated);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Delete_IndexOutOfRange_Throws(int index)
    {
        Assert.Throws<ArgumentException>(() => Alloc().Delete(index));
    }

    [Fact]
    public void ApplyTo_KeepsOthersAndReplacesAtFirstPosition()
    {
        var result = Alloc(120).ApplyTo(Sample(), Day);

        Assert.Equal(["other", "x", "y", "", "merged"], result.Select(l => l.Note));
        Assert.Equal([45, 60, 30, 30, 25], result.Select(l => l.Minutes));
        Assert.Equal("a", result[1].Sources[0].StopwatchId);
        Assert.Equal(2, result[4].Sources.Count);
    }

    [Fact]
    public void ApplyTo_NoUnallocated_DropsUnnotedLine()
    {
        var result = Alloc(90).ApplyTo(Sample(), Day);

        Assert.Equal(["other", "x", "y", "merged"], result.Select(l => l.Note));
    }

    [Fact]
    public void ApplyTo_NoOwnLines_Appends()
    {
        var others = new List<InvoiceLine> { L(45, "other", B) };
        var result = StopwatchDayAllocation.From(others, "a", "Alpha", 60).Allocate(60, "work").ApplyTo(others, Day);

        Assert.Equal(2, result.Count);
        Assert.Same(others[0], result[0]);
        Assert.Equal(60, result[1].Minutes);
        Assert.Equal("work", result[1].Note);
        Assert.Equal(Day, result[1].Date);
        Assert.Equal("Alpha", result[1].Sources[0].StopwatchName);
    }

    [Fact]
    public void ApplyTo_NothingTracked_AddsNothing()
    {
        var others = new List<InvoiceLine> { L(45, "other", B) };
        var result = StopwatchDayAllocation.From(others, "a", "Alpha", 0).ApplyTo(others, Day);

        Assert.Single(result);
    }

    [Fact]
    public void RoundTrip_FromApplyTo_EqualsAllocation()
    {
        var a = Alloc(150).Allocate(10, "z");
        var again = StopwatchDayAllocation.From(a.ApplyTo(Sample(), Day), "a", "Alpha", 150);

        Assert.Equal(a.Entries, again.Entries);
        Assert.Equal(a.Unallocated, again.Unallocated);
        Assert.Equal(a.BilledMinutes, again.BilledMinutes);
        Assert.Equal(a.OverMinutes, again.OverMinutes);
        Assert.Equal(a.MergedLines.Select(l => (l.Minutes, l.Note)), again.MergedLines.Select(l => (l.Minutes, l.Note)));
    }
}
