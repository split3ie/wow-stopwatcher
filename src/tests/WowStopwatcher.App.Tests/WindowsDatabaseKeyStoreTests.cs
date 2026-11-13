using System.Text;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Tests;

public sealed class WindowsDatabaseKeyStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wsw-keystore-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Round_trip_overwrite_and_delete()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsDatabaseKeyStore(_folder);
        var id = Guid.NewGuid();
        var first = DatabaseKey.Generate();
        var second = DatabaseKey.Generate();

        store.Save(id, first);
        Assert.True(first.KeyEquals(store.Get(id)));
        Assert.DoesNotContain(first.ToHex(), Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(_folder, id + ".key"))));

        store.Save(id, second);
        Assert.True(second.KeyEquals(store.Get(id)));
        Assert.Single(Directory.GetFiles(_folder));

        store.Delete(id);
        Assert.Null(store.Get(id));
        store.Delete(id);
    }

    [Fact]
    public void Missing_id_returns_null()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Null(new WindowsDatabaseKeyStore(_folder).Get(Guid.NewGuid()));
    }

    [Fact]
    public void Damaged_file_throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var id = Guid.NewGuid();
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, id + ".key"), [1, 2, 3, 4]);

        Assert.Throws<DatabaseKeyStoreException>(() => OperatingSystem.IsWindows() ? Read(id) : null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private DatabaseKey? Read(Guid id) => new WindowsDatabaseKeyStore(_folder).Get(id);
}
