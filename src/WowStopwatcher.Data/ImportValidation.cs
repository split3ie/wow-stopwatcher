namespace WowStopwatcher.Data;

/// <summary>
/// Result of checking a backup zip. When valid, <see cref="DatabasePath"/> and <see cref="TemplatesFolder"/> point into a temp folder
/// that is deleted on <see cref="Dispose"/>.
/// </summary>
public sealed class ImportValidation : IDisposable
{
    private readonly string? _tempFolder;

    internal ImportValidation(string? tempFolder, string? databasePath, string? templatesFolder, DatabaseInspection? inspection, TransferErrorCode? error)
    {
        _tempFolder = tempFolder;
        DatabasePath = databasePath;
        TemplatesFolder = templatesFolder;
        Inspection = inspection;
        Error = error;
    }

    /// <summary>True when the zip can be imported.</summary>
    public bool IsValid => Error is null;

    /// <summary>The error when not valid.</summary>
    public TransferErrorCode? Error { get; }

    /// <summary>The extracted database, when valid.</summary>
    public string? DatabasePath { get; }

    /// <summary>The extracted <c>templates</c> folder; it may not exist when the zip has none.</summary>
    public string? TemplatesFolder { get; }

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
