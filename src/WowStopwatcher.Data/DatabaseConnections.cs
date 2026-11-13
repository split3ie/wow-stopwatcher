using Microsoft.Data.Sqlite;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// Opens non-pooled connections, plain or encrypted. Encryption is SQLite3 Multiple Ciphers with the cipher pinned to
/// <see cref="Cipher"/> (so a future library default cannot change it) and a raw 256-bit key (<c>PRAGMA key = "x'...'"</c>):
/// the key is already random, so no password key derivation is needed, which keeps opening a connection fast.
/// </summary>
internal static class DatabaseConnections
{
    /// <summary>ChaCha20-Poly1305, the SQLite3 Multiple Ciphers default, set explicitly.</summary>
    public const string Cipher = "chacha20";

    /// <summary>Opens a connection and, with a <paramref name="key"/>, unlocks it. A wrong key surfaces on the first query (code 26), not here.</summary>
    public static SqliteConnection Open(string file, SqliteOpenMode mode, DatabaseKey? key, bool foreignKeys = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = mode,
            Pooling = false,
        }.ToString());

        try
        {
            connection.Open();
            if (key is not null)
            {
                Execute(connection, $"PRAGMA cipher = '{Cipher}';");
                Execute(connection, $"PRAGMA key = \"x'{key.ToHex()}'\";");
            }

            if (foreignKeys)
            {
                Execute(connection, "PRAGMA foreign_keys = ON;");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Encrypts the plain database of <paramref name="connection"/> in place with <paramref name="key"/>. It must not be in WAL mode.</summary>
    public static void Rekey(SqliteConnection connection, DatabaseKey key)
    {
        Execute(connection, $"PRAGMA cipher = '{Cipher}';");
        Execute(connection, $"PRAGMA rekey = \"x'{key.ToHex()}'\";");
    }

    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static int ReadVersion(SqliteConnection connection) => Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
