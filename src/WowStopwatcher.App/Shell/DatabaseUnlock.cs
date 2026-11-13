using Microsoft.Data.Sqlite;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Shell;

/// <summary>
/// Decides how the database is opened at startup. It is always opened encrypted:
/// <list type="number">
/// <item>Resolve the database path (<c>location.json</c>) and finish or undo an interrupted encryption (<see cref="DatabaseEncryption.Recover"/>).</item>
/// <item>Find the key of the database at the path (key file, then its id in the key store) and run the pending move, replace or import step with it;
/// then check the path again, because the step may have put another database there.</item>
/// <item>No database: make a key and a recovery code, show the "Protect your data" window, create the encrypted database.</item>
/// <item>Plain database: same window, then encrypt it in place (a backup is made first). A closed window or a failure leaves it untouched.</item>
/// <item>Encrypted database: the key from the key store opens it silently. A missing or rejected key, or an unavailable store, shows the
/// recovery code window; a correct code is saved to the store when possible. A missing or damaged key file is an error.</item>
/// </list>
/// The windows only collect input (<see cref="IStartupPrompts"/>). Returns null when the user closed a window or chose Quit.
/// <para>
/// Without <see cref="SecurityCapabilities.Encryption"/> (a build with the Encryption flag off) none of this runs: the pending step runs with no key,
/// a missing or plain database is opened as it always was, and an encrypted database (one with a key file) is refused with
/// <see cref="DatabaseProblem.KeyRequired"/> without touching anything. No key store, window or interrupted-encryption recovery is used.
/// </para>
/// </summary>
public sealed class DatabaseUnlock(
    DatabaseLocation location,
    DatabaseStartup startup,
    IDatabaseKeyStore keyStore,
    ShellOptions shellOptions,
    IStartupPrompts prompts,
    IStartupLanguage language,
    SecurityCapabilities security)
{
    /// <exception cref="DatabaseOpenException">The database cannot be used safely; nothing was changed.</exception>
    public async Task<UnlockResult?> RunAsync()
    {
        if (!security.Encryption)
        {
            return RunPlain();
        }

        var before = location.ResolveDatabasePath();
        var key = RecoverInterrupted(before);
        var outcome = startup.Run(key);

        var path = location.ResolveDatabasePath();
        if (!string.Equals(Path.GetFullPath(before), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            RecoverInterrupted(path);
        }

        DatabaseFileFormat format;
        try
        {
            format = DatabaseFormatDetector.Detect(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, path, "The database file could not be opened.", ex);
        }

        return format switch
        {
            DatabaseFileFormat.Missing => await ProtectAsync(path, outcome, isNew: true),
            DatabaseFileFormat.Plain => await ProtectAsync(path, outcome, isNew: false),
            _ => await OpenEncryptedAsync(path, outcome),
        };
    }

    /// <summary>The start of a build without encryption: the pending step with no key, then the database is left to be opened plain.</summary>
    private UnlockResult RunPlain()
    {
        var outcome = startup.Run();
        var path = location.ResolveDatabasePath();
        try
        {
            if (DatabaseFormatDetector.Detect(path) == DatabaseFileFormat.Encrypted && File.Exists(DatabaseKeyFile.PathFor(path)))
            {
                throw new DatabaseOpenException(DatabaseProblem.KeyRequired, path, "The database file is encrypted and this build cannot open encrypted data.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseOpenException(DatabaseProblem.Unavailable, path, "The database file could not be opened.", ex);
        }

        return new UnlockResult(null, null, outcome, null);
    }

    /// <summary>Finishes or undoes an interrupted encryption at <paramref name="path"/> and returns the key of the database there, if one is found.</summary>
    private DatabaseKey? RecoverInterrupted(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var key = Lookup(path).Key;
        EncryptionRecoveryResult result;
        try
        {
            result = new DatabaseEncryption(path).Recover(key);
        }
        catch (DatabaseEncryptionException ex)
        {
            throw new DatabaseOpenException(DatabaseProblem.EncryptionFailed, path, ex.Message, ex);
        }

        return result switch
        {
            EncryptionRecoveryResult.Conflict => throw new DatabaseOpenException(
                DatabaseProblem.EncryptionConflict, path, "Leftovers of an interrupted encryption are in an unexpected state."),
            EncryptionRecoveryResult.NothingToDo => key,
            _ => Lookup(path).Key, // the key file may have been removed by a rollback
        };
    }

    private async Task<UnlockResult?> ProtectAsync(string path, PendingOutcome? outcome, bool isNew)
    {
        // A plain database is probed read-only for its language; a new database has none, so the system language is used.
        language.Apply(isNew ? null : PlainDatabaseSettings.TryRead(path, LanguageResolver.SettingKey));

        var key = DatabaseKey.Generate();
        var databaseId = Guid.NewGuid();
        var code = RecoveryCode.Generate();
        // The key derivation (PBKDF2) takes a moment, so it runs off the UI thread.
        var contents = await Task.Run(() => new DatabaseKeyFileContents(databaseId, DatabaseKeyWrapper.Wrap(key, code)));

        StartupPinChoice? pinChoice = null;
        var request = new ProtectDataRequest(shellOptions.StartupPinStep, code, isNew, async pin =>
        {
            if (shellOptions.StartupPinStep)
            {
                pinChoice = new StartupPinChoice(pin);
            }

            await Task.Run(() => Protect(path, key, contents, isNew));
        });

        var result = await prompts.ShowProtectDataAsync(request);
        return result == ProtectDataOutcome.Done ? new UnlockResult(key, databaseId, outcome, pinChoice) : null;
    }

    /// <summary>
    /// Saves the key to the store first (so it is there even if the app stops right after the file is replaced), then creates or converts
    /// the database. If that fails the store entry is removed again.
    /// </summary>
    private void Protect(string path, DatabaseKey key, DatabaseKeyFileContents contents, bool isNew)
    {
        var saved = TrySave(contents.DatabaseId, key);
        try
        {
            if (isNew)
            {
                // No database is there, so a key file at this path is a leftover of an earlier attempt and is replaced.
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                DatabaseKeyFile.Write(path, contents, overwrite: true);
                _ = new SqliteDatabase(path, key);
            }
            else
            {
                new DatabaseEncryption(path).Encrypt(key, contents);
            }
        }
        catch (Exception ex) when (ex is DatabaseEncryptionException or IOException or UnauthorizedAccessException or SqliteException)
        {
            RemoveFromStore(contents.DatabaseId, saved);
            throw new DatabaseOpenException(DatabaseProblem.EncryptionFailed, path, ex.Message, ex);
        }
        catch
        {
            RemoveFromStore(contents.DatabaseId, saved);
            throw;
        }
    }

    private async Task<UnlockResult?> OpenEncryptedAsync(string path, PendingOutcome? outcome)
    {
        var lookup = Lookup(path);
        if (lookup.FileError is not null)
        {
            throw new DatabaseOpenException(DatabaseProblem.KeyFileDamaged, lookup.FileError.Path, lookup.FileError.Message, lookup.FileError);
        }

        if (lookup.File is null)
        {
            throw new DatabaseOpenException(DatabaseProblem.KeyFileMissing, DatabaseKeyFile.PathFor(path), "The key file next to the database is missing.");
        }

        var contents = lookup.File;
        var key = lookup.Key;
        if (key is not null && !DatabaseEncryption.KeyOpens(path, key))
        {
            key = null;
        }

        if (key is null)
        {
            key = await AskRecoveryCodeAsync(path, contents);
            if (key is null)
            {
                return null;
            }

            TrySave(contents.DatabaseId, key);
        }

        return new UnlockResult(key, contents.DatabaseId, outcome, null);
    }

    private async Task<DatabaseKey?> AskRecoveryCodeAsync(string path, DatabaseKeyFileContents contents)
    {
        language.Apply(null);

        DatabaseKey? unlocked = null;
        var request = new RecoveryPromptRequest(path, code => Task.Run(() =>
        {
            if (!RecoveryCode.IsValid(code))
            {
                return false;
            }

            var candidate = DatabaseKeyWrapper.TryUnwrap(contents.Wrapped, code);
            if (candidate is null || !DatabaseEncryption.KeyOpens(path, candidate))
            {
                return false;
            }

            unlocked = candidate;
            return true;
        }));

        var result = await prompts.ShowRecoveryPromptAsync(request);
        return result == RecoveryPromptOutcome.Unlocked ? unlocked : null;
    }

    /// <summary>Reads the key file next to <paramref name="path"/> and looks its id up in the key store.</summary>
    private KeyLookup Lookup(string path)
    {
        DatabaseKeyFileContents? file;
        try
        {
            file = DatabaseKeyFile.Read(path);
        }
        catch (DatabaseKeyFileException ex)
        {
            return new KeyLookup(null, null, ex);
        }

        DatabaseKey? key = null;
        if (file is not null && keyStore.IsAvailable)
        {
            try
            {
                key = keyStore.Get(file.DatabaseId);
            }
            catch (DatabaseKeyStoreException)
            {
                // An unreadable store is the same as no entry: the recovery code window follows.
            }
        }

        return new KeyLookup(file, key, null);
    }

    /// <summary>Saves the key when a store is available. A failure is not fatal: the recovery code is asked for at the next start.</summary>
    private bool TrySave(Guid databaseId, DatabaseKey key)
    {
        if (!keyStore.IsAvailable)
        {
            return false;
        }

        try
        {
            keyStore.Save(databaseId, key);
            return true;
        }
        catch (DatabaseKeyStoreException)
        {
            return false;
        }
    }

    private void RemoveFromStore(Guid databaseId, bool saved)
    {
        if (!saved)
        {
            return;
        }

        try
        {
            keyStore.Delete(databaseId);
        }
        catch (DatabaseKeyStoreException)
        {
            // Best effort: an unused entry is harmless.
        }
    }

    private sealed record KeyLookup(DatabaseKeyFileContents? File, DatabaseKey? Key, DatabaseKeyFileException? FileError);
}
