using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data;

/// <summary>Reads one setting from a plain (not encrypted) database without migrating or writing anything.</summary>
public static class PlainDatabaseSettings
{
    /// <summary>The stored value, or null when the file is missing, not plain, has no such setting or cannot be read.</summary>
    public static string? TryRead(string databasePath, string key)
    {
        try
        {
            if (DatabaseFormatDetector.Detect(databasePath) != DatabaseFileFormat.Plain)
            {
                return null;
            }

            using var connection = DatabaseConnections.Open(databasePath, SqliteOpenMode.ReadOnly, null);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
