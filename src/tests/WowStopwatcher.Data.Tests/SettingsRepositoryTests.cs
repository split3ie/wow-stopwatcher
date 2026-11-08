namespace WowStopwatcher.Data.Tests;

public class SettingsRepositoryTests
{
    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        using var db = new TempDatabase();
        Assert.Null(new SqliteSettingsRepository(db.Database).Get("missing"));
    }

    [Fact]
    public void SetThenGet_ReturnsValue()
    {
        using var db = new TempDatabase();
        var settings = new SqliteSettingsRepository(db.Database);
        settings.Set("k", "v");
        Assert.Equal("v", settings.Get("k"));
    }

    [Fact]
    public void Set_OverwritesExistingValue()
    {
        using var db = new TempDatabase();
        var settings = new SqliteSettingsRepository(db.Database);
        settings.Set("k", "one");
        settings.Set("k", "two");
        Assert.Equal("two", settings.Get("k"));
    }
}
