using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceReconcilerTests
{
    private static readonly DateOnly D1 = new(2026, 11, 1);
    private static readonly DateOnly D2 = new(2026, 11, 2);
    private static readonly DateOnly D3 = new(2026, 11, 3);

    private static InvoiceLine L(DateOnly date, int minutes, string note = "") =>
        new(date, minutes, note, [new InvoiceLineSource("a", "Alpha")]);

    private static InvoiceDay Live(DateOnly date, int minutes, bool running = false) =>
        new(date, false, [L(date, minutes)], minutes, running);

    [Fact]
    public void NoStoredDays_ReturnsLiveDays()
    {
        var result = InvoiceReconciler.Compose([Live(D1, 30), Live(D2, 20)], []);

        Assert.Equal([D1, D2], result.Select(d => d.Date));
        Assert.All(result, d => Assert.False(d.Edited));
    }

    [Fact]
    public void StoredDayWins_LinesAndEdited_ButTrackedFromLive()
    {
        var stored = new StoredInvoiceDay(D1, true, [L(D1, 50, "n")]);

        var day = Assert.Single(InvoiceReconciler.Compose([Live(D1, 30, running: true)], [stored]));

        Assert.True(day.Edited);
        Assert.Equal(50, day.InvoicedMinutes);
        Assert.Equal(30, day.TrackedMinutes);
        Assert.True(day.HasRunningSession);
        Assert.Equal(-20, day.DriftMinutes);
    }

    [Fact]
    public void StoredDayWithoutLive_HasZeroTrackedAndNoRunning()
    {
        var day = Assert.Single(InvoiceReconciler.Compose([], [new StoredInvoiceDay(D2, true, [L(D2, 40)])]));

        Assert.Equal(0, day.TrackedMinutes);
        Assert.False(day.HasRunningSession);
        Assert.Equal(-40, day.DriftMinutes);
    }

    [Fact]
    public void FrozenDay_NotEdited_HasNoDrift()
    {
        var day = Assert.Single(InvoiceReconciler.Compose([Live(D1, 30)], [new StoredInvoiceDay(D1, false, [L(D1, 50)])]));

        Assert.False(day.Edited);
        Assert.Equal(0, day.DriftMinutes);
        Assert.Equal(50, day.InvoicedMinutes);
    }

    [Fact]
    public void Union_IsOrderedByDate()
    {
        var result = InvoiceReconciler.Compose(
            [Live(D3, 10), Live(D1, 10)],
            [new StoredInvoiceDay(D2, true, [L(D2, 5)])]);

        Assert.Equal([D1, D2, D3], result.Select(d => d.Date));
    }

    [Fact]
    public void IsCustom_IsTrueOnlyWithoutSources()
    {
        Assert.True(new InvoiceLine(D1, 10, "", []).IsCustom);
        Assert.False(L(D1, 10).IsCustom);
    }

    [Fact]
    public void StoredCustomLineWithoutLiveDay_IsKeptAndHasNoDrift()
    {
        var stored = new StoredInvoiceDay(D2, true, [new InvoiceLine(D2, 40, "n", [])]);

        var day = Assert.Single(InvoiceReconciler.Compose([], [stored]));

        Assert.True(day.Edited);
        Assert.Equal(40, day.InvoicedMinutes);
        Assert.Equal(40, day.BilledMinutes);
        Assert.Equal(0, day.TrackedMinutes);
        Assert.Equal(0, day.DriftMinutes);
    }

    [Fact]
    public void Drift_ExcludesCustomMinutes()
    {
        var stored = new StoredInvoiceDay(D1, true, [L(D1, 20), new InvoiceLine(D1, 60, "", [])]);

        var day = Assert.Single(InvoiceReconciler.Compose([Live(D1, 30)], [stored]));

        Assert.Equal(80, day.InvoicedMinutes);
        Assert.Equal(80, day.UnallocatedMinutes);
        Assert.Equal(10, day.DriftMinutes);
    }
}
