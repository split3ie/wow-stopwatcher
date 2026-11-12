using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// Result of checking a backup file (a plain <c>.zip</c> or a password-protected <c>.wsbackup</c>). When valid, <see cref="DatabasePath"/> and <see cref="TemplatesFolder"/> point into a temp folder
/// that is deleted on <see cref="Dispose"/>.
/// </summary>
public sealed class ImportValidation : IDisposable
{
    private readonly string? _tempFolder;

    internal ImportValidation(string? tempFolder, string? databasePath, string? templatesFolder, DatabaseInspection? inspection, TransferErrorCode? error, DatabaseKey? key = null, DatabaseKeyFileContents? keyFile = null)
    {
        _tempFolder = tempFolder;
        DatabasePath = databasePath;
        TemplatesFolder = templatesFolder;
        Inspection = inspection;
        Error = error;
        Key = key;
        KeyFile = keyFile;
    }

    /// <summary>True when the file can be imported.</summary>
    public bool IsValid => Error is null;

    /// <summary>The error when not valid.</summary>
    public TransferErrorCode? Error { get; }

    /// <summary>The extracted database, when valid.</summary>
    public string? DatabasePath { get; }

    /// <summary>The extracted <c>templates</c> folder; it may not exist when the backup has none.</summary>
    public string? TemplatesFolder { get; }

    /// <summary>
    /// The database key stored in a <c>.wsbackup</c>; null for a plain zip (its database is plain). The App stores it in the OS key store
    /// before the restart that applies the import.
    /// </summary>
    public DatabaseKey? Key { get; }

    /// <summary>The key file contents stored in a <c>.wsbackup</c> (also extracted next to <see cref="DatabasePath"/>); null for a plain zip.</summary>
    public DatabaseKeyFileContents? KeyFile { get; }

    /// <summary>Summary of the contained database, when it could be read.</summary>
    public DatabaseInspection? Inspection { get; }

    /// <summary>Deletes the extracted temp folder.</summary>
    public void Dispose()
    {
        if (_tempFolder is not null)
        {
            try
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the temp folder is removed by the OS eventually.
            }
        }
    }
}
