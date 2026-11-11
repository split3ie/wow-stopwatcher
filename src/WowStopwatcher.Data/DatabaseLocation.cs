using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowStopwatcher.Data;

/// <summary>
/// Reads and writes <c>location.json</c> in the default folder: where the database lives and an optional pending step.
/// The file is written atomically (temp file in the same folder, then rename). A damaged file is reported by
/// <see cref="Read"/> and makes <see cref="ResolveDatabasePath"/> throw; <see cref="ResolveDefaultDatabasePath"/> is the explicit fallback.
/// </summary>
public sealed class DatabaseLocation
{
    /// <summary>File name of the database inside a folder.</summary>
    public const string DatabaseFileName = "wowstopwatcher.db";

    /// <summary>File name of the location settings inside the default folder.</summary>
    public const string LocationFileName = "location.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Uses the folder of <see cref="SqliteDatabase.DefaultPath"/> as the default folder.</summary>
    public DatabaseLocation()
        : this(Path.GetDirectoryName(SqliteDatabase.DefaultPath)!)
    {
    }

    /// <summary>Uses <paramref name="defaultFolder"/> as the default folder (tests pass a temp folder).</summary>
    public DatabaseLocation(string defaultFolder)
    {
        DefaultFolder = Path.GetFullPath(defaultFolder);
        LocationFilePath = Path.Combine(DefaultFolder, LocationFileName);
    }

    /// <summary>The default data folder; it holds <c>location.json</c>.</summary>
    public string DefaultFolder { get; }

    /// <summary>Full path of <c>location.json</c>.</summary>
    public string LocationFilePath { get; }

    /// <summary>Full path of the database in the default folder.</summary>
    public string DefaultDatabasePath => Path.Combine(DefaultFolder, DatabaseFileName);

    /// <summary>Reads <c>location.json</c>. A missing file or missing keys mean the default folder.</summary>
    public LocationReadResult Read()
    {
        string json;
        try
        {
            if (!File.Exists(LocationFilePath))
            {
                return new LocationReadResult(new DatabaseLocationSettings(null, null), null);
            }

            json = File.ReadAllText(LocationFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Damaged($"{LocationFileName} could not be read: {ex.Message}");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<LocationDto>(json, JsonOptions);
            if (dto is null)
            {
                return Damaged($"{LocationFileName} is empty.");
            }

            string? folder = null;
            if (!string.IsNullOrWhiteSpace(dto.DatabaseFolder))
            {
                if (!Path.IsPathRooted(dto.DatabaseFolder))
                {
                    return Damaged($"{LocationFileName} has a database folder that is not an absolute path.");
                }

                folder = Path.GetFullPath(dto.DatabaseFolder);
            }

            PendingStep? pending = null;
            if (dto.Pending is { } p)
            {
                if (p.Kind is null || string.IsNullOrWhiteSpace(p.Source) || string.IsNullOrWhiteSpace(p.Target))
                {
                    return Damaged($"{LocationFileName} has an incomplete pending step.");
                }

                pending = new PendingStep(p.Kind.Value, p.Source, p.Target);
            }

            return new LocationReadResult(new DatabaseLocationSettings(folder, pending), null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return Damaged($"{LocationFileName} is damaged: {ex.Message}");
        }
    }

    /// <summary>
    /// The database path: the configured folder plus <see cref="DatabaseFileName"/>, or the default path when none is configured.
    /// Throws <see cref="DatabaseOpenException"/> (<see cref="DatabaseProblem.LocationDamaged"/>) when <c>location.json</c> is damaged.
    /// </summary>
    public string ResolveDatabasePath()
    {
        var result = Read();
        if (result.IsDamaged)
        {
            throw new DatabaseOpenException(DatabaseProblem.LocationDamaged, LocationFilePath, result.Problem!);
        }

        return result.Settings.DatabaseFolder is { } folder ? Path.Combine(folder, DatabaseFileName) : DefaultDatabasePath;
    }

    /// <summary>The default database path, ignoring <c>location.json</c>. Use it only after an explicit user choice.</summary>
    public string ResolveDefaultDatabasePath() => DefaultDatabasePath;

    /// <summary>True when a folder is configured and does not exist (unplugged drive, sync not finished).</summary>
    public bool IsConfiguredFolderMissing()
    {
        var result = Read();
        return !result.IsDamaged && result.Settings.DatabaseFolder is { } folder && !Directory.Exists(folder);
    }

    /// <summary>
    /// Sets the database folder; null (or the default folder itself) means the default. Keeps a pending step.
    /// A damaged file is replaced, which is an explicit reset.
    /// </summary>
    public void SetFolder(string? folder)
    {
        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var full = Path.GetFullPath(folder);
            normalized = SamePath(full, DefaultFolder) ? null : full;
        }

        Write(Read().Settings with { DatabaseFolder = normalized });
    }

    /// <summary>Stores the step to run at the next startup. Keeps the configured folder.</summary>
    public void SetPending(PendingStep step) => Write(Read().Settings with { Pending = step });

    /// <summary>Removes the pending step. Keeps the configured folder.</summary>
    public void ClearPending()
    {
        var current = Read().Settings;
        if (current.Pending is not null)
        {
            Write(current with { Pending = null });
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static LocationReadResult Damaged(string problem) => new(new DatabaseLocationSettings(null, null), problem);

    private void Write(DatabaseLocationSettings settings)
    {
        Directory.CreateDirectory(DefaultFolder);
        var dto = new LocationDto
        {
            DatabaseFolder = settings.DatabaseFolder,
            Pending = settings.Pending is { } p ? new PendingDto { Kind = p.Kind, Source = p.Source, Target = p.Target } : null,
        };

        var temp = LocationFilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(temp, LocationFilePath, overwrite: true);
    }

    private sealed class LocationDto
    {
        public string? DatabaseFolder { get; set; }

        public PendingDto? Pending { get; set; }
    }

    private sealed class PendingDto
    {
        public PendingKind? Kind { get; set; }

        public string? Source { get; set; }

        public string? Target { get; set; }
    }
}
