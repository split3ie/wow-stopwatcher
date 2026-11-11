using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceMergeAcrossDaysTests
{
    private static readonly DateOnly Mon = new(2026, 11, 2);
    private static readonly DateOnly Tue = new(2026, 11, 3);
    private static readonly DateOnly Wed = new(2026, 11, 4);
    private static readonly DateOnly Fri = new(2026, 11, 6);
    private static readonly InvoiceLineSource A = new("a", "Alpha");
    private static readonly InvoiceLineSource B = new("b", "Beta");

    private static InvoiceLine L(DateOnly date, int minutes, InvoiceLineSource source, string note = "") =>
        new(date, minutes, note, [source]);

    private static Dictionary<DateOnly, IReadOnlyList<InvoiceLine>> Days(params (DateOnly Date, InvoiceLine[] Lines)[] days) =>
        days.ToDictionary(d => d.Date, d => (IReadOnlyList<InvoiceLine>)d.Lines);

    private static IReadOnlyList<StoredInvoiceDay> Stored(IEnumerable<(DateOnly Date, IReadOnlyList<InvoiceLine> Lines)> merged) =>
        merged.Select(m => new StoredInvoiceDay(m.Date, true, m.Lines)).ToList();

    private static InvoiceDay Live(DateOnly date, int minutes) =>
        new(date, false, [L(date, minutes, A)], minutes, false);

    [Fact]
    public void Merges_into_the_target_day_and_remembers_the_moved_time()
    {
        var days = Days((Mon, [L(Mon, 480, A, "work")]), (Tue, [L(Tue, 240, B, "more")]));

        var result = InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0)], Mon);

        Assert.Equal([Mon, Tue], result.Select(r => r.Date));
        var merged = Assert.Single(result[0].Lines);
        Assert.Equal(720, merged.Minutes);
        Assert.Equal(Mon, merged.Date);
        Assert.Equal("work; more", merged.Note);
        Assert.Equal(["a", "b"], merged.Sources.Select(s => s.StopwatchId));
        Assert.Equal([new InvoiceLineMove(Tue, 240)], merged.MovedIn);
        Assert.Empty(result[1].Lines);
    }

    [Fact]
    public void Drift_is_zero_on_both_days_after_the_merge()
    {
        var days = Days((Mon, [L(Mon, 480, A)]), (Tue, [L(Tue, 240, A)]));
        var merged = Stored(InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0)], Mon));

        var composed = InvoiceReconciler.Compose([Live(Mon, 480), Live(Tue, 240)], merged);

        Assert.All(composed, d => Assert.Equal(0, d.DriftMinutes));
        Assert.Equal(240, composed[1].MovedOutMinutes);
        Assert.True(composed[1].Edited);
    }

    [Fact]
    public void Entries_from_the_target_day_are_dropped_when_merging_back()
    {
        var days = Days((Mon, [L(Mon, 720, A) with { MovedIn = [new InvoiceLineMove(Tue, 240)] }]), (Tue, [L(Tue, 60, B)]));

        var result = InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0)], Tue);

        var merged = Assert.Single(result[1].Lines);
        Assert.Equal(780, merged.Minutes);
        Assert.Equal([new InvoiceLineMove(Mon, 480)], merged.MovedIn);
        Assert.Empty(result[0].Lines);
    }

    [Fact]
    public void Re_merging_a_merged_line_keeps_earlier_moves_and_combines_by_date()
    {
        var merged = L(Tue, 300, A) with { MovedIn = [new InvoiceLineMove(Wed, 100)] };
        var days = Days((Mon, [L(Mon, 480, A)]), (Tue, [merged]), (Wed, [L(Wed, 50, B)]));

        var result = InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0), (Wed, 0)], Mon);

        var line = Assert.Single(result[0].Lines);
        Assert.Equal(830, line.Minutes);
        Assert.Equal([new InvoiceLineMove(Tue, 200), new InvoiceLineMove(Wed, 150)], line.MovedIn);
    }

    [Fact]
    public void Merged_line_takes_the_place_of_the_first_selected_line_of_the_target_day()
    {
        var days = Days((Mon, [L(Mon, 10, A), L(Mon, 20, B)]), (Tue, [L(Tue, 30, A), L(Tue, 40, B)]));

        var result = InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 1), (Tue, 0)], Mon);

        Assert.Equal([10, 50], result[0].Lines.Select(l => l.Minutes));
        Assert.Equal([40], result[1].Lines.Select(l => l.Minutes));
    }

    [Fact]
    public void A_gap_a_custom_line_a_foreign_target_or_one_line_throws()
    {
        var days = Days((Mon, [L(Mon, 10, A)]), (Wed, [L(Wed, 10, A)]), (Tue, [new InvoiceLine(Tue, 5, "c", [])]));

        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Wed, 0)], Mon));
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0)], Mon));
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0), (Tue, 0)], Wed));
        Assert.Throws<ArgumentException>(() => InvoiceLineEditor.MergeAcrossDays(days, [(Mon, 0)], Mon));
    }

    [Fact]
    public void Split_keeps_moves_on_the_first_part_only()
    {
        var line = L(Mon, 720, A) with { MovedIn = [new InvoiceLineMove(Tue, 240)] };

        var result = InvoiceLineEditor.Split([line], 0, 400);

        Assert.Equal([new InvoiceLineMove(Tue, 240)], result[0].MovedIn);
        Assert.Empty(result[1].MovedIn);
    }

    [Fact]
    public void Same_day_merge_unites_moves_and_combines_by_date()
    {
        var first = L(Mon, 100, A) with { MovedIn = [new InvoiceLineMove(Tue, 30)] };
        var second = L(Mon, 80, B) with { MovedIn = [new InvoiceLineMove(Tue, 20), new InvoiceLineMove(Wed, 10)] };

        var result = InvoiceLineEditor.Merge([first, second], [0, 1]);

        Assert.Equal([new InvoiceLineMove(Tue, 50), new InvoiceLineMove(Wed, 10)], Assert.Single(result).MovedIn);
    }

    [Fact]
    public void Adjust_and_note_keep_moves()
    {
        var line = L(Mon, 100, A) with { MovedIn = [new InvoiceLineMove(Tue, 30)] };

        Assert.Equal([new InvoiceLineMove(Tue, 30)], InvoiceLineEditor.Adjust([line], 0, 90)[0].MovedIn);
        Assert.Equal([new InvoiceLineMove(Tue, 30)], InvoiceLineEditor.SetNote([line], 0, "x")[0].MovedIn);
    }

    [Fact]
    public void Allocation_puts_the_moves_of_the_replaced_lines_on_the_first_replacement()
    {
        var lines = new[]
        {
            L(Mon, 100, A) with { MovedIn = [new InvoiceLineMove(Tue, 30)] },
            L(Mon, 20, B),
            L(Mon, 40, A) with { MovedIn = [new InvoiceLineMove(Tue, 10), new InvoiceLineMove(Wed, 5)] },
        };
        var allocation = StopwatchDayAllocation.From(lines, "a", "Alpha", 200).Allocate(50, "note");

        var result = allocation.ApplyTo(lines, Mon);

        Assert.Equal([new InvoiceLineMove(Tue, 40), new InvoiceLineMove(Wed, 5)], result.First(l => l.Sources[0].StopwatchId == "a").MovedIn);
        Assert.Equal(1, result.Count(l => l.MovedIn.Count > 0));
    }

    [Fact]
    public void A_reset_source_day_shows_negative_drift_so_double_billing_is_visible()
    {
        var target = new StoredInvoiceDay(Mon, true, [L(Mon, 720, A) with { MovedIn = [new InvoiceLineMove(Tue, 240)] }]);

        var composed = InvoiceReconciler.Compose([Live(Mon, 480), Live(Tue, 240)], [target]);

        Assert.Equal(0, composed[0].DriftMinutes);
        Assert.False(composed[1].Edited);
        Assert.Equal(240, composed[1].MovedOutMinutes);
        Assert.Equal(-240, composed[1].DriftMinutes);
    }

    [Fact]
    public void Neighbouring_days_helper()
    {
        Assert.True(InvoiceLineEditor.AreNeighbouringDays([Mon]));
        Assert.True(InvoiceLineEditor.AreNeighbouringDays([Wed, Mon, Tue, Mon]));
        Assert.False(InvoiceLineEditor.AreNeighbouringDays([Mon, Wed]));
        Assert.False(InvoiceLineEditor.AreNeighbouringDays([Mon, Fri]));
        Assert.False(InvoiceLineEditor.AreNeighbouringDays([]));
    }
}
