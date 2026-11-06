using WowStopwatcher.Core;

namespace WowStopwatcher.Data.Tests;

/// <summary>A database in a unique temp folder, deleted on dispose. Never touches the real AppData database.</summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public TempDatabase()
    {
        Database = new SqliteDatabase(Path.Combine(_folder, "sub", "test.db"));
        Stopwatches = new SqliteStopwatchRepository(Database);
        Sessions = new SqliteSessionRepository(Database);
    }

    public SqliteDatabase Database { get; }

    public SqliteStopwatchRepository Stopwatches { get; }

    public SqliteSessionRepository Sessions { get; }

    public StopwatchDefinition AddStopwatch(string id, int order = 0, TimeSpan? goal = null)
    {
        var def = new StopwatchDefinition(id, id.ToUpperInvariant(), "#2F6FEB", goal, null, order);
        Stopwatches.Add(def);
        return def;
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
