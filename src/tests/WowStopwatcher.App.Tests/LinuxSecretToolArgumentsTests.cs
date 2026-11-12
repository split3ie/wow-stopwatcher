using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Tests;

public class LinuxSecretToolArgumentsTests
{
    [Fact]
    public void Arguments_carry_service_and_database_id_but_no_secret()
    {
        var id = Guid.NewGuid();

        Assert.Equal(["store", "--label=WowStopwatcher database key", "service", "com.split3ie.wowstopwatcher.dbkey", "database", id.ToString("D")], LinuxSecretToolArguments.Store(id));
        Assert.Equal(["lookup", "service", "com.split3ie.wowstopwatcher.dbkey", "database", id.ToString("D")], LinuxSecretToolArguments.Lookup(id));
        Assert.Equal(["clear", "service", "com.split3ie.wowstopwatcher.dbkey", "database", id.ToString("D")], LinuxSecretToolArguments.Clear(id));
    }

    [Fact]
    public void Unavailable_store_reports_unavailable()
    {
        var store = new UnavailableDatabaseKeyStore();

        Assert.False(store.IsAvailable);
        Assert.Null(store.Get(Guid.NewGuid()));
        Assert.Throws<DatabaseKeyStoreException>(() => store.Delete(Guid.NewGuid()));
    }
}
