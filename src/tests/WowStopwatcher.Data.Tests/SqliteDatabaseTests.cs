namespace WowStopwatcher.Data.Tests;

public class SqliteDatabaseTests
{
    [Fact]
    public void CreatesFolderFileAndSchema()
    {
        using var db = new TempDatabase();
        Assert.True(File.Exists(db.Database.Path));
        Assert.Equal(SqliteDatabase.LatestSchemaVersion, db.Database.GetSchemaVersion());
    }

    [Fact]
    public void Migration_IsIdempotent_AndKeepsData()
    {
        using var db = new TempDatabase();
        db.AddStopwatch("a");

        var again = new SqliteDatabase(db.Database.Path);
        var again2 = new SqliteDatabase(db.Database.Path);

        Assert.Equal(SqliteDatabase.LatestSchemaVersion, again2.GetSchemaVersion());
        Assert.Single(new SqliteStopwatchRepository(again).GetAll());
    }

    [Fact]
    public void ForeignKeysAreEnforced()
    {
        using var db = new TempDatabase();
        Assert.ThrowsAny<Exception>(() => db.Sessions.InsertOpen("missing", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DefaultPath_IsInAppData()
    {
        Assert.EndsWith(Path.Combine("WowStopwatcher", "wowstopwatcher.db"), SqliteDatabase.DefaultPath);
    }
}
