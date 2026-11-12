using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Keys in the Secret Service (GNOME Keyring, KWallet) through the <c>secret-tool</c> command. The secret goes through stdin, never an argument.
/// Available only when <c>secret-tool</c> exists and answers a lookup without an error (checked once).
/// </summary>
public sealed class LinuxDatabaseKeyStore : IDatabaseKeyStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Lazy<bool> _available;

    public LinuxDatabaseKeyStore() => _available = new Lazy<bool>(Probe);

    public bool IsAvailable => _available.Value;

    public DatabaseKey? Get(Guid databaseId)
    {
        var result = Run(LinuxSecretToolArguments.Lookup(databaseId), null);
        if (result.ExitCode != 0)
        {
            if (string.IsNullOrWhiteSpace(result.Error))
            {
                return null;
            }

            throw new DatabaseKeyStoreException("The Secret Service could not be read. It may be locked or unavailable.");
        }

        return DatabaseKey.FromHex(result.Output.Trim()) ?? throw new DatabaseKeyStoreException("The Secret Service entry for the database key is damaged.");
    }

    public void Save(Guid databaseId, DatabaseKey key)
    {
        var result = Run(LinuxSecretToolArguments.Store(databaseId), key.ToHex());
        if (result.ExitCode != 0)
        {
            throw new DatabaseKeyStoreException("The Secret Service refused to store the database key. It may be locked or unavailable.");
        }
    }

    public void Delete(Guid databaseId)
    {
        var result = Run(LinuxSecretToolArguments.Clear(databaseId), null);
        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Error))
        {
            throw new DatabaseKeyStoreException("The Secret Service could not delete the database key. It may be locked or unavailable.");
        }
    }

    private bool Probe()
    {
        try
        {
            // A lookup of an id that cannot exist: exit 1 with no message means "not found", which proves the service answers.
            var result = Run(LinuxSecretToolArguments.Lookup(Guid.NewGuid()), null);
            return result.ExitCode == 0 || string.IsNullOrWhiteSpace(result.Error);
        }
        catch (DatabaseKeyStoreException)
        {
            return false;
        }
    }

    private static (int ExitCode, string Output, string Error) Run(IReadOnlyList<string> arguments, string? stdin)
    {
        var info = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new DatabaseKeyStoreException("secret-tool could not be started.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
            }

            process.StandardInput.Close();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill(entireProcessTree: true);
                throw new DatabaseKeyStoreException("secret-tool did not answer in time.");
            }

            process.WaitForExit();
            return (process.ExitCode, output.Result, error.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            throw new DatabaseKeyStoreException("secret-tool (libsecret-tools) is not installed or cannot be run.", ex);
        }
    }
}
