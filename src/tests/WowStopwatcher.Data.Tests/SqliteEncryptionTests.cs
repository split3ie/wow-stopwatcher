using Microsoft.Data.Sqlite;

namespace WowStopwatcher.Data.Tests;

public sealed class SqliteEncryptionTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public SqliteEncryptionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_folder, name);

    private static string ConnectionString(string path, string? password) =>
        new SqliteConnectionStringBuilder { DataSource = path, Password = password, Pooling = false }.ToString();

    private static SqliteConnection Open(string path, string? password)
    {
        var connection = new SqliteConnection(ConnectionString(path, password));
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string CreateEncrypted(string name)
    {
        var path = PathFor(name);
        using var connection = Open(path, Password);
        Exec(connection, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)");
        Exec(connection, "INSERT INTO t (v) VALUES ('secret-value')");
        return path;
    }

    [Fact]
    public void EncryptedDatabase_ReopensWithSamePassword()
    {
        var path = CreateEncrypted("enc.db");

        using var connection = Open(path, Password);

        Assert.Equal("secret-value", ScalarText(connection, "SELECT v FROM t"));
    }

    [Fact]
    public void EncryptedDatabase_FailsWithoutPassword()
    {
        var path = CreateEncrypted("nopass.db");

        var ex = Assert.Throws<SqliteException>(() =>
        {
            using var connection = Open(path, null);
            Scalar(connection, "SELECT count(*) FROM t");
        });

        // SQLITE_NOTADB (26) is expected; the code is asserted as a non-OK failure and recorded in the spike report.
        Assert.Equal(26, ex.SqliteErrorCode);
    }

    [Fact]
    public void EncryptedDatabase_FailsWithWrongPassword()
    {
        var path = CreateEncrypted("wrong.db");

        var ex = Assert.Throws<SqliteException>(() =>
        {
            using var connection = Open(path, "wrong password");
            Scalar(connection, "SELECT count(*) FROM t");
        });

        Assert.Equal(26, ex.SqliteErrorCode);
    }

    [Fact]
    public void EncryptedDatabase_FileDoesNotStartWithPlainHeader()
    {
        var path = CreateEncrypted("header.db");

        var header = new byte[16];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.ReadExactly(header);
        }

        var plain = "SQLite format 3\0"u8.ToArray();
        Assert.NotEqual(plain, header);
    }

    [Fact]
    public void EncryptedDatabase_SupportsWal()
    {
        var path = PathFor("wal.db");
        using var connection = Open(path, Password);

        Assert.Equal("wal", ScalarText(connection, "PRAGMA journal_mode = WAL"));
        Exec(connection, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)");
        Exec(connection, "INSERT INTO t (v) VALUES ('x')");
        Assert.Equal(1, Scalar(connection, "SELECT count(*) FROM t"));
    }

    [Fact]
    public void BackupFromPlainDirectlyToEncrypted_IsRejected()
    {
        var plainPath = PathFor("plain-direct.db");
        using (var plain = Open(plainPath, null))
        {
            Exec(plain, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)");
        }

        using var source = Open(plainPath, null);
        using var destination = Open(PathFor("direct.db"), Password);

        // Finding: the backup API refuses plain -> encrypted ("incompatible source and target databases", SQLITE_ERROR 1).
        var ex = Assert.Throws<SqliteException>(() => source.BackupDatabase(destination));
        Assert.Equal(1, ex.SqliteErrorCode);
    }

    [Fact]
    public void BackupPlainToPlainThenRekey_ProducesReadableEncryptedCopy()
    {
        var plainPath = PathFor("plain.db");
        using (var plain = Open(plainPath, null))
        {
            Exec(plain, "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)");
            Exec(plain, "INSERT INTO t (v) VALUES ('from-plain')");
        }

        var copyPath = PathFor("copy.db");
        using (var source = Open(plainPath, null))
        using (var destination = Open(copyPath, null))
        {
            source.BackupDatabase(destination);
        }

        using (var copy = Open(copyPath, null))
        {
            Exec(copy, $"PRAGMA rekey = '{Password}'");
        }

        var header = new byte[16];
        using (var stream = new FileStream(copyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.ReadExactly(header);
        }
        Assert.NotEqual("SQLite format 3\0"u8.ToArray(), header);

        using var check = Open(copyPath, Password);
        Assert.Equal("from-plain", ScalarText(check, "SELECT v FROM t"));
        Assert.Equal("ok", ScalarText(check, "PRAGMA integrity_check"));
    }

    [Fact]
    public void BackupEncryptedToEncrypted_ProducesReadableCopy()
    {
        var path = CreateEncrypted("src-enc.db");
        var copyPath = PathFor("dst-enc.db");
        using (var source = Open(path, Password))
        using (var destination = Open(copyPath, Password))
        {
            source.BackupDatabase(destination);
        }

        using var check = Open(copyPath, Password);
        Assert.Equal("secret-value", ScalarText(check, "SELECT v FROM t"));
        Assert.Equal("ok", ScalarText(check, "PRAGMA integrity_check"));
    }
}
