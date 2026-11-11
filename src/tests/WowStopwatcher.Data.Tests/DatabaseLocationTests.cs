namespace WowStopwatcher.Data.Tests;

public sealed class DatabaseLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowsw-location-" + Guid.NewGuid().ToString("N"));

    private string DefaultFolder => Path.Combine(_root, "default");

    private DatabaseLocation Create() => new(DefaultFolder);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void NoFile_ResolvesToDefaultPath()
    {
        var location = Create();

        Assert.False(location.Read().IsDamaged);
        Assert.Equal(Path.Combine(DefaultFolder, "wowstopwatcher.db"), location.ResolveDatabasePath());
        Assert.False(location.IsConfiguredFolderMissing());
    }

    [Fact]
    public void RoundTrip_FolderAndPending()
    {
        var location = Create();
        var folder = Path.Combine(_root, "elsewhere");
        var step = new PendingStep(PendingKind.Replace, Path.Combine(_root, "a.db"), Path.Combine(folder, "wowstopwatcher.db"));

        location.SetFolder(folder);
        location.SetPending(step);

        var read = Create().Read();
        Assert.Equal(folder, read.Settings.DatabaseFolder);
        Assert.Equal(step, read.Settings.Pending);
        Assert.Equal(Path.Combine(folder, "wowstopwatcher.db"), Create().ResolveDatabasePath());

        location.ClearPending();
        read = Create().Read();
        Assert.Null(read.Settings.Pending);
        Assert.Equal(folder, read.Settings.DatabaseFolder);

        location.SetFolder(null);
        Assert.Null(Create().Read().Settings.DatabaseFolder);
    }

    [Fact]
    public void SetFolder_ToDefaultFolder_StoresNull()
    {
        var location = Create();
        location.SetFolder(DefaultFolder);

        Assert.Null(location.Read().Settings.DatabaseFolder);
    }

    [Fact]
    public void JsonShape_UsesDocumentedKeys()
    {
        var location = Create();
        location.SetFolder(Path.Combine(_root, "x"));
        location.SetPending(new PendingStep(PendingKind.Move, "s", Path.Combine(_root, "t")));

        var json = File.ReadAllText(location.LocationFilePath);
        Assert.Contains("\"databaseFolder\"", json);
        Assert.Contains("\"pending\"", json);
        Assert.Contains("\"kind\": \"move\"", json);
    }

    [Fact]
    public void Write_IsAtomic_NoTempLeftAndStaleTempIgnored()
    {
        var location = Create();
        Directory.CreateDirectory(DefaultFolder);
        File.WriteAllText(location.LocationFilePath + ".tmp", "stale garbage");

        location.SetFolder(Path.Combine(_root, "x"));

        Assert.False(File.Exists(location.LocationFilePath + ".tmp"));
        Assert.False(location.Read().IsDamaged);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"databaseFolder\": \"relative\\\\path\"}")]
    [InlineData("{\"pending\": {\"kind\": \"explode\", \"source\": \"a\", \"target\": \"b\"}}")]
    [InlineData("{\"pending\": {\"kind\": \"move\"}}")]
    public void DamagedFile_IsReported_NotSilentlyDefault(string content)
    {
        var location = Create();
        Directory.CreateDirectory(DefaultFolder);
        File.WriteAllText(location.LocationFilePath, content);

        Assert.True(location.Read().IsDamaged);
        var ex = Assert.Throws<DatabaseOpenException>(() => location.ResolveDatabasePath());
        Assert.Equal(DatabaseProblem.LocationDamaged, ex.Problem);
        Assert.Equal(Path.Combine(DefaultFolder, "wowstopwatcher.db"), location.ResolveDefaultDatabasePath());
        Assert.False(location.IsConfiguredFolderMissing());
    }

    [Fact]
    public void DamagedFile_ExplicitSetFolderNull_Resets()
    {
        var location = Create();
        Directory.CreateDirectory(DefaultFolder);
        File.WriteAllText(location.LocationFilePath, "{ nope");

        location.SetFolder(null);

        Assert.False(location.Read().IsDamaged);
    }

    [Fact]
    public void MissingConfiguredFolder_IsDetected()
    {
        var location = Create();
        var folder = Path.Combine(_root, "unplugged");
        location.SetFolder(folder);

        Assert.True(location.IsConfiguredFolderMissing());
        Assert.False(Directory.Exists(folder));

        Directory.CreateDirectory(folder);
        Assert.False(location.IsConfiguredFolderMissing());
    }
}
