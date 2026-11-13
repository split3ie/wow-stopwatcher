namespace WowStopwatcher.App.Shell;

/// <summary>The <c>secret-tool</c> (libsecret-tools) argument lists used by <see cref="LinuxDatabaseKeyStore"/>.</summary>
public static class LinuxSecretToolArguments
{
    public const string Service = "com.split3ie.wowstopwatcher.dbkey";

    public static IReadOnlyList<string> Store(Guid databaseId) =>
        ["store", "--label=WowStopwatcher database key", "service", Service, "database", databaseId.ToString("D")];

    public static IReadOnlyList<string> Lookup(Guid databaseId) =>
        ["lookup", "service", Service, "database", databaseId.ToString("D")];

    public static IReadOnlyList<string> Clear(Guid databaseId) =>
        ["clear", "service", Service, "database", databaseId.ToString("D")];
}
