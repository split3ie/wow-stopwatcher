using WowStopwatcher.Core;

namespace WowStopwatcher.Data.Tests;

public class StopwatchRepositoryTests
{
    [Fact]
    public void AddAndGet_RoundTripsAllFields()
    {
        using var db = new TempDatabase();
        var def = new StopwatchDefinition("a", "English", "#2F6FEB", TimeSpan.FromHours(5), "Ctrl+F1", 3);
        db.Stopwatches.Add(def);

        Assert.Equal(def, db.Stopwatches.Get("a"));
    }

    [Fact]
    public void IsIndependent_RoundTripsOnAddAndUpdate()
    {
        using var db = new TempDatabase();
        var def = new StopwatchDefinition("a", "YouTube", "#111111", null, null, 0, IsIndependent: true);
        db.Stopwatches.Add(def);
        Assert.Equal(def, db.Stopwatches.Get("a"));
        Assert.True(Assert.Single(db.Stopwatches.GetAll()).IsIndependent);

        db.Stopwatches.Update(def with { IsIndependent = false });
        Assert.False(db.Stopwatches.Get("a")!.IsIndependent);
    }

    [Fact]
    public void NullableFields_RoundTripAsNull()
    {
        using var db = new TempDatabase();
        var def = new StopwatchDefinition("a", "Walking", "#111111", null, null, 0);
        db.Stopwatches.Add(def);
        Assert.Equal(def, db.Stopwatches.Get("a"));
    }

    [Fact]
    public void Update_OverwritesFields()
    {
        using var db = new TempDatabase();
        var def = db.AddStopwatch("a");
        db.Stopwatches.Update(def with { Name = "New", Goal = TimeSpan.FromMinutes(45), Shortcut = "Ctrl+K" });
        var loaded = db.Stopwatches.Get("a")!;
        Assert.Equal("New", loaded.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), loaded.Goal);
        Assert.Equal("Ctrl+K", loaded.Shortcut);
    }

    [Fact]
    public void SoftDelete_HidesFromDefaultListButKeepsRowAndSessions()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        db.AddStopwatch("b", 1);
        db.Sessions.InsertOpen("a", DateTimeOffset.UtcNow);

        db.Stopwatches.SoftDelete("a");

        Assert.Equal(["b"], db.Stopwatches.GetAll().Select(s => s.Id));
        Assert.Equal(["a", "b"], db.Stopwatches.GetAll(includeDeleted: true).Select(s => s.Id));
        Assert.True(db.Stopwatches.Get("a")!.IsDeleted);
        Assert.Single(db.Sessions.GetOpen());
    }

    [Fact]
    public void SaveSortOrder_AppliesIndexOrder()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a", 0);
        db.AddStopwatch("b", 1);
        db.AddStopwatch("c", 2);

        db.Stopwatches.SaveSortOrder(["c", "a", "b"]);

        Assert.Equal(["c", "a", "b"], db.Stopwatches.GetAll().Select(s => s.Id));
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");
        Assert.ThrowsAny<Exception>(() => db.AddStopwatch("a"));
    }
}
