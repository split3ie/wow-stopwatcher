namespace WowStopwatcher.App.Shell;

/// <summary>Picks the secret store of the running operating system.</summary>
public static class DatabaseKeyStoreFactory
{
    public static IDatabaseKeyStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDatabaseKeyStore(WindowsDatabaseKeyStore.DefaultFolder());
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacDatabaseKeyStore();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxDatabaseKeyStore();
        }

        return new UnavailableDatabaseKeyStore();
    }
}
