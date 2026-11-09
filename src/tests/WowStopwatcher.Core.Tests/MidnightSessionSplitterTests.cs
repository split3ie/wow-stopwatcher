namespace WowStopwatcher.Core.Tests;

public class MidnightSessionSplitterTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static (FakeSessionRepository Repo, MidnightSessionSplitter Splitter, Session Session) Setup(DateTimeOffset start)
    {
        var repo = new FakeSessionRepository();
        var id = repo.InsertOpen("a", start);
        return (repo, new MidnightSessionSplitter(repo), repo.Items.Single(s => s.Id == id));
    }

    [Fact]
    public void SameDay_ReturnsSameSessionAndWritesNothing()
    {
        var start = new DateTimeOffset(2026, 3, 9, 22, 0, 0, TimeSpan.Zero);
        var (repo, splitter, session) = Setup(start);

        var result = splitter.Split(session, start.AddHours(1), new DateOnly(2026, 3, 9), Zone);

        Assert.Equal(session, result);
        Assert.Single(repo.Items);
        Assert.True(repo.Items[0].IsOpen);
    }

    [Fact]
    public void CrossedOneMidnight_ClosesAtMidnightAndReopens()
    {
        var start = new DateTimeOffset(2026, 3, 9, 23, 0, 0, TimeSpan.Zero);
        var midnight = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var (repo, splitter, session) = Setup(start);

        var result = splitter.Split(session, start.AddHours(2), new DateOnly(2026, 3, 10), Zone);

        Assert.Equal(midnight, result.StartUtc);
        Assert.Null(result.StopUtc);
        Assert.Equal("a", result.StopwatchId);
        Assert.Equal(2, repo.Items.Count);
        Assert.Equal(midnight, repo.Items[0].StopUtc);
        Assert.True(repo.Items.Single(s => s.Id == result.Id).IsOpen);
    }

    [Fact]
    public void CrossedSeveralMidnights_SplitsAtEachOne()
    {
        var start = new DateTimeOffset(2026, 3, 9, 23, 0, 0, TimeSpan.Zero);
        var (repo, splitter, session) = Setup(start);

        var result = splitter.Split(session, start.AddDays(2), new DateOnly(2026, 3, 11), Zone);

        Assert.Equal(new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero), result.StartUtc);
        Assert.Equal(3, repo.Items.Count);
        Assert.Equal(2, repo.Items.Count(s => !s.IsOpen));
    }

    [Fact]
    public void TodayAheadOfNow_DoesNotSplitPastNow()
    {
        var start = new DateTimeOffset(2026, 3, 9, 23, 0, 0, TimeSpan.Zero);
        var (repo, splitter, session) = Setup(start);

        var result = splitter.Split(session, start.AddMinutes(30), new DateOnly(2026, 3, 10), Zone);

        Assert.Equal(session, result);
        Assert.Single(repo.Items);
    }
}
