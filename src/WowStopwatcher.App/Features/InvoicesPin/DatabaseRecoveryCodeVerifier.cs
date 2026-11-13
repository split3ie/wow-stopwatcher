using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>A recovery code is right when it unwraps the key file next to the open database to the key the database is open with.</summary>
public sealed class DatabaseRecoveryCodeVerifier(SqliteDatabase database, IDatabaseKeyProvider keys) : IRecoveryCodeVerifier
{
    public Task<bool> VerifyAsync(string code) => Task.Run(() =>
    {
        if (!RecoveryCode.IsValid(code) || keys.Key is not { } key)
        {
            return false;
        }

        DatabaseKeyFileContents? file;
        try
        {
            file = DatabaseKeyFile.Read(database.Path);
        }
        catch (DatabaseKeyFileException)
        {
            return false;
        }

        return file is not null && DatabaseKeyWrapper.TryUnwrap(file.Wrapped, code) is { } unwrapped && unwrapped.KeyEquals(key);
    });
}
