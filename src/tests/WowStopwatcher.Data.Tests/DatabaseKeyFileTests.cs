using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data.Tests;

public sealed class DatabaseKeyFileTests : EncryptionTestBase
{
    [Fact]
    public void PathFor_UsesDatabaseNameWithKeyJsonExtension()
    {
        Assert.Equal(Path.Combine(Folder, "wowstopwatcher.key.json"), DatabaseKeyFile.PathFor(DbPath));
    }

    [Fact]
    public void WriteThenRead_RoundTripsAndKeyStillUnwraps()
    {
        var key = DatabaseKey.Generate();
        var code = RecoveryCode.Generate();
        var contents = new DatabaseKeyFileContents(Guid.NewGuid(), DatabaseKeyWrapper.Wrap(key, code, iterations: 1000));

        DatabaseKeyFile.Write(DbPath, contents);
        var read = DatabaseKeyFile.Read(DbPath);

        Assert.NotNull(read);
        Assert.Equal(contents.DatabaseId, read.DatabaseId);
        Assert.True(key.KeyEquals(DatabaseKeyWrapper.TryUnwrap(read.Wrapped, code)));
        Assert.Empty(Directory.GetFiles(Folder, "*.tmp"));
    }

    [Fact]
    public void Read_ReturnsNullWhenMissing()
    {
        Assert.Null(DatabaseKeyFile.Read(DbPath));
    }

    [Fact]
    public void Write_DoesNotOverwriteUnlessAsked()
    {
        var first = NewKeyFile(DatabaseKey.Generate());
        var second = NewKeyFile(DatabaseKey.Generate());
        DatabaseKeyFile.Write(DbPath, first);

        Assert.Throws<IOException>(() => DatabaseKeyFile.Write(DbPath, second));
        Assert.Equal(first.DatabaseId, DatabaseKeyFile.Read(DbPath)!.DatabaseId);

        DatabaseKeyFile.Write(DbPath, second, overwrite: true);
        Assert.Equal(second.DatabaseId, DatabaseKeyFile.Read(DbPath)!.DatabaseId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"version\":1,\"databaseId\":\"00000000-0000-0000-0000-000000000001\",\"kdf\":\"PBKDF2-SHA256\",\"iterations\":1,\"salt\":\"!!\",\"nonce\":\"AA==\",\"ciphertext\":\"AA==\",\"tag\":\"AA==\"}")]
    [InlineData("{\"version\":2,\"databaseId\":\"00000000-0000-0000-0000-000000000001\"}")]
    public void Read_DamagedOrUnknownVersion_ThrowsKeyFileException(string json)
    {
        File.WriteAllText(DatabaseKeyFile.PathFor(DbPath), json);

        Assert.Throws<DatabaseKeyFileException>(() => DatabaseKeyFile.Read(DbPath));
    }
}
