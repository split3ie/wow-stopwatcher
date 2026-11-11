using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.Core.Tests.Invoicing;

public class InvoiceLineBuilderTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("p2", TimeSpan.FromHours(2), "p2", "p2");
    private static readonly InvoicePeriod November = new(2026, 11);
    private static readonly DateTimeOffset Now = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string> Names = new() { ["a"] = "Alpha", ["b"] = "Beta", ["x"] = "Other" };
    private static readonly HashSet<string> Assigned = ["a", "b"];

    private static Session S(string sw, DateTimeOffset start, TimeSpan length) => new(1, sw, start, start + length);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 11, day, hour, minute, 0, TimeSpan.Zero);

    private static IReadOnlyList<InvoiceDay> Build(params Session[] sessions) =>
        InvoiceLineBuilder.Build(sessions, Names, Assigned, November, Zone, Now);

    [Fact]
    public void MultipleDays_OneDayPerDateOrderedByDate()
    {
        var days = Build(
            S("a", At(5, 8), TimeSpan.FromMinutes(30)),
            S("a", At(3, 8), TimeSpan.FromMinutes(60)));

        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 5)], days.Select(d => d.Date));
        Assert.All(days, d => Assert.False(d.Edited));
        Assert.Equal([60, 30], days.Select(d => d.TrackedMinutes));
    }

    [Fact]
    public void TwoStopwatches_LinesOrderedByFirstUseThatDay_AndSessionsSummed()
    {
        var days = Build(
            S("b", At(3, 9), TimeSpan.FromMinutes(20)),
            S("a", At(3, 10), TimeSpan.FromMinutes(10)),
            S("b", At(3, 11), TimeSpan.FromMinutes(15)));

        var day = Assert.Single(days);
        Assert.Equal(["b", "a"], day.Lines.Select(l => l.Sources[0].StopwatchId));
        Assert.Equal([35, 10], day.Lines.Select(l => l.Minutes));
        Assert.Equal(["Beta", "Alpha"], day.Lines.Select(l => l.Sources[0].StopwatchName));
        Assert.Equal(45, day.TrackedMinutes);
        Assert.All(day.Lines, l => Assert.Equal(string.Empty, l.Note));
    }

    [Fact]
    public void UnassignedStopwatch_IsExcluded()
    {
        Assert.Empty(Build(S("x", At(3, 9), TimeSpan.FromMinutes(20))));
    }

    [Fact]
    public void Rounding_HalfMinuteGoesUp_AndZeroMinuteLinesAreDropped()
    {
        var days = Build(
            S("a", At(3, 9), TimeSpan.FromSeconds(90)),
            S("b", At(3, 10), TimeSpan.FromSeconds(29)),
            S("b", At(4, 10), TimeSpan.FromSeconds(10)));

        var day = Assert.Single(days);
        var line = Assert.Single(day.Lines);
        Assert.Equal(2, line.Minutes);
        Assert.Equal("a", line.Sources[0].StopwatchId);
    }

    [Fact]
    public void RoundingHappensPerLine_NotPerSession()
    {
        var days = Build(
            S("a", At(3, 9), TimeSpan.FromSeconds(40)),
            S("a", At(3, 10), TimeSpan.FromSeconds(40)));

        Assert.Equal(1, Assert.Single(Assert.Single(days).Lines).Minutes);
    }

    [Fact]
    public void OpenSession_CountsUpToNow_AndSetsRunningFlag()
    {
        var start = Now.AddMinutes(-90);
        var days = InvoiceLineBuilder.Build(
            [new Session(1, "a", start, null)], Names, Assigned, new InvoicePeriod(2026, 12), Zone, Now);

        var day = Assert.Single(days);
        Assert.True(day.HasRunningSession);
        Assert.Equal(90, day.TrackedMinutes);
    }

    [Fact]
    public void ClosedSessions_DoNotSetRunningFlag()
    {
        Assert.False(Assert.Single(Build(S("a", At(3, 9), TimeSpan.FromMinutes(5)))).HasRunningSession);
    }

    [Fact]
    public void SessionsOutsidePeriod_AreIgnored()
    {
        var days = Build(
            S("a", At(3, 9).AddMonths(-1), TimeSpan.FromMinutes(30)),
            S("a", At(3, 9).AddMonths(1), TimeSpan.FromMinutes(30)));

        Assert.Empty(days);
    }

    [Fact]
    public void DayIsLocalDateOfStart_NotUtc()
    {
        // 2026-11-30 23:30 UTC is 2026-12-01 01:30 local (+2): December, not November.
        var lateNovember = S("a", At(30, 23, 30), TimeSpan.FromMinutes(30));
        // 2026-10-31 23:30 UTC is 2026-11-01 01:30 local: November.
        var earlyNovember = S("a", new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30));

        var day = Assert.Single(Build(lateNovember, earlyNovember));

        Assert.Equal(new DateOnly(2026, 11, 1), day.Date);
    }

    [Fact]
    public void SessionCrossingMidnight_IsClippedIntoBothDays()
    {
        // Starts 21:30 UTC = 23:30 local and runs 60 minutes into the next local day.
        var days = Build(S("a", At(3, 21, 30), TimeSpan.FromMinutes(60)));

        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 4)], days.Select(d => d.Date));
        Assert.Equal([30, 30], days.Select(d => d.TrackedMinutes));
    }

    [Fact]
    public void UnknownName_FallsBackToId()
    {
        var days = InvoiceLineBuilder.Build(
            [S("a", At(3, 9), TimeSpan.FromMinutes(5))],
            new Dictionary<string, string>(), Assigned, November, Zone, Now);

        Assert.Equal("a", days[0].Lines[0].Sources[0].StopwatchName);
    }
}
