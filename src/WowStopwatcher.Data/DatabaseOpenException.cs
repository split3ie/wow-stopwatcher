namespace WowStopwatcher.Data;

/// <summary>The database file cannot be used safely. The file has not been modified.</summary>
public sealed class DatabaseOpenException : Exception
{
    public DatabaseOpenException(DatabaseProblem problem, string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Problem = problem;
        Path = path;
    }

    public DatabaseProblem Problem { get; }

    /// <summary>Full path of the database file.</summary>
    public string Path { get; }
}
