using Microsoft.Data.Sqlite;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

public sealed class DatabaseStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowstopwatcher-startup-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseLocation _location;
    private readonly DatabaseStartup _startup;

    public DatabaseStartupTests()
    {
        _location = new DatabaseLocation(Path.Combine(_root, "default"));
        _startup = new DatabaseStartup(_location, new DatabaseTransfer(TimeProvider.System));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp folder.
        }
    }

    [Fact]
    public void NothingConfigured_ReturnsNull()
    {
        Assert.Null(_startup.Run());
    }

    [Fact]
    public void ConfiguredFolderMissing_ThrowsAndCreatesNothing()
    {
        var missing = Path.Combine(_root, "unplugged");
        _location.SetFolder(missing);

        var ex = Assert.Throws<DatabaseOpenException>(() => _startup.Run());

        Assert.Equal(DatabaseProblem.FolderMissing, ex.Problem);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void DamagedLocationFile_Throws()
    {
        Directory.CreateDirectory(_location.DefaultFolder);
        File.WriteAllText(_location.LocationFilePath, "{ not json");

        var ex = Assert.Throws<DatabaseOpenException>(() => _startup.Run());

        Assert.Equal(DatabaseProblem.LocationDamaged, ex.Problem);
    }

    [Fact]
    public void DamagedLocationFile_UseDefaultResets()
    {
        Directory.CreateDirectory(_location.DefaultFolder);
        File.WriteAllText(_location.LocationFilePath, "{ not json");

        _location.SetFolder(null);

        Assert.Null(_startup.Run());
    }

    [Fact]
    public void PendingMove_IsAppliedBeforeOpening()
    {
        var source = new SqliteDatabase(_location.DefaultDatabasePath).Path;
        var target = Path.Combine(_root, "moved", DatabaseLocation.DatabaseFileName);
        _location.SetPending(new PendingStep(PendingKind.Move, source, target));

        var outcome = _startup.Run();

        Assert.NotNull(outcome);
        Assert.True(outcome.Success);
        Assert.True(File.Exists(target));
        Assert.Equal(target, _location.ResolveDatabasePath());
        Assert.Null(_location.Read().Settings.Pending);
    }

    [Fact]
    public void FailedPendingStep_IsReportedOnceAndKeepsCurrentDatabase()
    {
        var source = Path.Combine(_root, "does-not-exist.db");
        var target = Path.Combine(_root, "moved", DatabaseLocation.DatabaseFileName);
        _location.SetPending(new PendingStep(PendingKind.Move, source, target));

        var outcome = _startup.Run();

        Assert.NotNull(outcome);
        Assert.False(outcome.Success);
        Assert.Equal(_location.DefaultDatabasePath, _location.ResolveDatabasePath());
        Assert.Null(_startup.Run());
    }
}
