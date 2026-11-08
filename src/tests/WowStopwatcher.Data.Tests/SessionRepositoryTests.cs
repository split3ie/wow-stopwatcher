using WowStopwatcher.Core;

namespace WowStopwatcher.Data.Tests;

public class SessionRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InsertOpen_ThenClose_RoundTrips()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");

        var id = db.Sessions.InsertOpen("a", T0);
        var open = Assert.Single(db.Sessions.GetOpen());
        Assert.Equal(new Session(id, "a", T0, null), open);

        db.Sessions.Close(id, T0.AddMinutes(5));
        Assert.Empty(db.Sessions.GetOpen());
        Assert.Equal(T0.AddMinutes(5), db.Sessions.GetLatest()!.StopUtc);
    }

    [Fact]
    public void GetLatestForStopwatches_OnlyConsidersGivenIds()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.AddStopwatch("b", 1);
        db.Sessions.InsertOpen("a", T0);
        db.Sessions.InsertOpen("b", T0.AddHours(1));

        Assert.Null(db.Sessions.GetLatestForStopwatches([]));
        Assert.Null(db.Sessions.GetLatestForStopwatches(["missing"]));
        Assert.Equal("a", db.Sessions.GetLatestForStopwatches(["a"])!.StopwatchId);
        Assert.Equal("b", db.Sessions.GetLatestForStopwatches(["a", "b"])!.StopwatchId);
    }

    [Fact]
    public void GetLatest_IsNullWhenEmpty_ElseLatestStart()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.AddStopwatch("b", 1);
        Assert.Null(db.Sessions.GetLatest());
        db.Sessions.InsertOpen("a", T0);
        db.Sessions.InsertOpen("b", T0.AddHours(1));
        Assert.Equal("b", db.Sessions.GetLatest()!.StopwatchId);
    }

    [Fact]
    public void GetInRange_IsHalfOpenAndOrdered()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.Sessions.InsertOpen("a", T0.AddHours(2));
        db.Sessions.InsertOpen("a", T0);
        db.Sessions.InsertOpen("a", T0.AddHours(5));

        var result = db.Sessions.GetInRange(T0, T0.AddHours(5));

        Assert.Equal([T0, T0.AddHours(2)], result.Select(s => s.StartUtc));
    }

    [Fact]
    public void GetLocalDays_UsesZone_DistinctNewestFirst()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        var zone = TimeZoneInfo.CreateCustomTimeZone("p3", TimeSpan.FromHours(3), "p3", "p3");
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero)); // local 11th
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 11, 5, 0, 0, TimeSpan.Zero));  // local 11th
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero));  // local 9th

        Assert.Equal([new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 9)], db.Sessions.GetLocalDays(zone));
    }

    [Fact]
    public void DeleteForStopwatchInRange_OnlyDeletesMatching()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.AddStopwatch("b", 1);
        db.Sessions.InsertOpen("a", T0);
        db.Sessions.InsertOpen("b", T0);
        db.Sessions.InsertOpen("a", T0.AddDays(1));

        var deleted = db.Sessions.DeleteForStopwatchInRange("a", T0.AddHours(-1), T0.AddHours(1));

        Assert.Equal(1, deleted);
        Assert.Equal(2, db.Sessions.GetInRange(T0.AddDays(-5), T0.AddDays(5)).Count);
    }

    [Fact]
    public void DeleteForLocalDays_DeletesWholeDays()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero));
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 10, 23, 59, 0, TimeSpan.Zero));
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero));
        db.Sessions.InsertOpen("a", new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero));

        var deleted = db.Sessions.DeleteForLocalDays([new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12)], TimeZoneInfo.Utc);

        Assert.Equal(3, deleted);
        Assert.Equal([new DateOnly(2026, 3, 11)], db.Sessions.GetLocalDays(TimeZoneInfo.Utc));
    }

    [Fact]
    public void DeleteAll_RemovesEverything()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.Sessions.InsertOpen("a", T0);
        db.Sessions.InsertOpen("a", T0.AddHours(1));
        Assert.Equal(2, db.Sessions.DeleteAll());
        Assert.Null(db.Sessions.GetLatest());
    }

    [Fact]
    public void Milliseconds_ArePreserved()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        var start = T0.AddMilliseconds(123);
        db.Sessions.InsertOpen("a", start);
        Assert.Equal(start, db.Sessions.GetOpen()[0].StartUtc);
    }
}
