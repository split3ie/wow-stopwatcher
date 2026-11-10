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
    public void IsPermanentAndActiveSince_RoundTripOnAddAndUpdate()
    {
        using var db = new TempDatabase();
        var since = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var def = new StopwatchDefinition("a", "English", "#111111", null, null, 0, IsPermanent: true, ActiveSinceUtc: since);
        db.Stopwatches.Add(def);
        Assert.Equal(def, db.Stopwatches.Get("a"));
        Assert.Equal(def, Assert.Single(db.Stopwatches.GetAll()));

        db.Stopwatches.Update(def with { IsPermanent = false, ActiveSinceUtc = since.AddDays(1) });
        var updated = db.Stopwatches.Get("a")!;
        Assert.False(updated.IsPermanent);
        Assert.Equal(since.AddDays(1), updated.ActiveSinceUtc);
    }

    [Fact]
    public void GoalSince_RoundTripsOnAddAndUpdate()
    {
        using var db = new TempDatabase();
        var since = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var def = new StopwatchDefinition("a", "English", "#111111", TimeSpan.FromHours(1), null, 0, GoalSinceUtc: since);
        db.Stopwatches.Add(def);
        Assert.Equal(since, db.Stopwatches.Get("a")!.GoalSinceUtc);

        db.Stopwatches.Update(def with { GoalSinceUtc = since.AddDays(2) });
        Assert.Equal(since.AddDays(2), db.Stopwatches.Get("a")!.GoalSinceUtc);

        db.Stopwatches.Update(def with { GoalSinceUtc = null });
        Assert.Null(db.Stopwatches.Get("a")!.GoalSinceUtc);
    }

    [Fact]
    public void GoalPerCycle_RoundTripsOnAddAndUpdate()
    {
        using var db = new TempDatabase();
        var def = new StopwatchDefinition("a", "English", "#111111", TimeSpan.FromHours(1), null, 0, GoalPerCycle: true);
        db.Stopwatches.Add(def);
        Assert.True(db.Stopwatches.Get("a")!.GoalPerCycle);

        db.Stopwatches.Update(def with { GoalPerCycle = false });
        Assert.False(db.Stopwatches.Get("a")!.GoalPerCycle);
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

    [Fact]
    public void Update_WithDifferentGoal_RecordsChangeWithPreviousGoal()
    {
        using var db = new TempDatabase();
        var def = db.AddStopwatch("a", goal: TimeSpan.FromHours(6));
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);

        db.Stopwatches.Update(def with { Goal = TimeSpan.FromHours(8) });

        var change = Assert.Single(db.Stopwatches.GetGoalChanges());
        Assert.Equal("a", change.StopwatchId);
        Assert.Equal(TimeSpan.FromHours(6), change.PreviousGoal);
        Assert.InRange(change.ChangedUtc, before, DateTimeOffset.UtcNow.AddSeconds(2));
    }

    [Fact]
    public void Update_GoalAddedOrRemoved_RecordsNullAwareChanges()
    {
        using var db = new TempDatabase();
        var def = db.AddStopwatch("a");

        db.Stopwatches.Update(def with { Goal = TimeSpan.FromHours(1) });
        db.Stopwatches.Update(def with { Goal = null });

        var changes = db.Stopwatches.GetGoalChanges();
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[0].PreviousGoal);
        Assert.Equal(TimeSpan.FromHours(1), changes[1].PreviousGoal);
    }

    [Fact]
    public void Update_WithSameGoal_RecordsNothing()
    {
        using var db = new TempDatabase();
        var def = db.AddStopwatch("a", goal: TimeSpan.FromHours(6));

        db.Stopwatches.Update(def with { Name = "Renamed" });

        Assert.Empty(db.Stopwatches.GetGoalChanges());
    }

    private static SqliteStopwatchRepository InLanguage(TempDatabase db, string language) =>
        new(db.Database, null, () => language);

    private static StopwatchDefinition Def(string name) => new("a", name, "#2F6FEB", null, null, 0);

    [Fact]
    public void Names_CreatedInLanguage_FallBackToOriginalInOtherLanguage()
    {
        using var db = new TempDatabase();
        InLanguage(db, "en").Add(Def("English"));

        Assert.Equal("English", InLanguage(db, "en").Get("a")!.Name);
        Assert.Equal("English", InLanguage(db, "uk").Get("a")!.Name);
        Assert.Equal("English", Assert.Single(InLanguage(db, "uk").GetAll()).Name);
    }

    [Fact]
    public void Names_RenameInOtherLanguage_KeepsOriginal()
    {
        using var db = new TempDatabase();
        InLanguage(db, "en").Add(Def("English"));
        var uk = InLanguage(db, "uk");

        uk.Update(Def("Anhliiska"));
        uk.Update(Def("Anhliiska 2"));

        Assert.Equal("Anhliiska 2", uk.Get("a")!.Name);
        Assert.Equal("Anhliiska 2", Assert.Single(uk.GetAll()).Name);
        Assert.Equal("English", InLanguage(db, "en").Get("a")!.Name);
        Assert.Equal("English", InLanguage(db, "de").Get("a")!.Name);
    }

    [Fact]
    public void Names_SavingOtherFieldInOtherLanguage_DoesNotPinFallbackName()
    {
        using var db = new TempDatabase();
        InLanguage(db, "en").Add(Def("English"));
        var uk = InLanguage(db, "uk");

        uk.Update(uk.Get("a")! with { Color = "#111111" });
        InLanguage(db, "en").Update(Def("Learning"));

        Assert.Equal("Learning", uk.Get("a")!.Name);
    }

    [Fact]
    public void Names_RenameInOriginalLanguage_UpdatesFallback()
    {
        using var db = new TempDatabase();
        InLanguage(db, "en").Add(Def("English"));
        InLanguage(db, "uk").Update(Def("Anhliiska"));

        InLanguage(db, "en").Update(Def("Learning"));

        Assert.Equal("Learning", InLanguage(db, "en").Get("a")!.Name);
        Assert.Equal("Learning", InLanguage(db, "de").Get("a")!.Name);
        Assert.Equal("Anhliiska", InLanguage(db, "uk").Get("a")!.Name);
    }

    [Fact]
    public void Names_LegacyUnknownLanguage_FirstRenameClaimsOriginal()
    {
        using var db = new TempDatabase();
        db.Stopwatches.Add(Def("English"));

        InLanguage(db, "uk").Update(Def("Anhliiska"));

        Assert.Equal("Anhliiska", InLanguage(db, "uk").Get("a")!.Name);
        Assert.Equal("Anhliiska", InLanguage(db, "en").Get("a")!.Name);

        InLanguage(db, "en").Update(Def("English again"));
        Assert.Equal("English again", InLanguage(db, "en").Get("a")!.Name);
        Assert.Equal("Anhliiska", InLanguage(db, "uk").Get("a")!.Name);
    }

    [Fact]
    public void Names_WithoutLanguageFunc_UseOnlyOriginalName()
    {
        using var db = new TempDatabase();
        db.Stopwatches.Add(Def("English"));
        db.Stopwatches.Update(Def("Renamed"));

        Assert.Equal("Renamed", db.Stopwatches.Get("a")!.Name);
        Assert.Equal("Renamed", InLanguage(db, "uk").Get("a")!.Name);
    }
}
