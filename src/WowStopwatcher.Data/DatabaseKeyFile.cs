using System.Text.Json;
using System.Text.Json.Serialization;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Data;

/// <summary>
/// Reads and writes <c>&lt;database name&gt;.key.json</c> next to a database file (for <c>wowstopwatcher.db</c>: <c>wowstopwatcher.key.json</c>).
/// It holds the database id and the database key wrapped by the recovery code. The file is written atomically
/// (temp file in the same folder, then rename) and an existing file is replaced only on request.
/// </summary>
public static class DatabaseKeyFile
{
    /// <summary>Format version written to the file.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The key file path for a database file.</summary>
    public static string PathFor(string databasePath)
    {
        var full = Path.GetFullPath(databasePath);
        return Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".key.json");
    }

    /// <summary>
    /// Writes the key file. Throws <see cref="IOException"/> when it already exists and <paramref name="overwrite"/> is false.
    /// </summary>
    public static void Write(string databasePath, DatabaseKeyFileContents contents, bool overwrite = false)
    {
        var path = PathFor(databasePath);
        if (!overwrite && File.Exists(path))
        {
            throw new IOException($"The key file already exists: {path}");
        }

        var wrapped = contents.Wrapped;
        var dto = new KeyFileDto
        {
            Version = CurrentVersion,
            DatabaseId = contents.DatabaseId,
            Kdf = wrapped.Kdf,
            Iterations = wrapped.Iterations,
            Salt = Convert.ToBase64String(wrapped.Salt),
            Nonce = Convert.ToBase64String(wrapped.Nonce),
            Ciphertext = Convert.ToBase64String(wrapped.Ciphertext),
            Tag = Convert.ToBase64String(wrapped.Tag),
        };

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(dto, JsonOptions));
            File.Move(temp, path, overwrite);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Best effort: a leftover temp file is harmless.
            }

            throw;
        }
    }

    /// <summary>
    /// Reads the key file. Returns null when there is none; throws <see cref="DatabaseKeyFileException"/> when it cannot be read,
    /// is damaged or has an unknown version.
    /// </summary>
    public static DatabaseKeyFileContents? Read(string databasePath)
    {
        var path = PathFor(databasePath);
        string json;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseKeyFileException(path, "The key file could not be read.", ex);
        }

        KeyFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<KeyFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new DatabaseKeyFileException(path, "The key file is damaged.", ex);
        }

        if (dto is null)
        {
            throw new DatabaseKeyFileException(path, "The key file is damaged.");
        }

        if (dto.Version != CurrentVersion)
        {
            throw new DatabaseKeyFileException(path, $"The key file has format version {dto.Version}, which this app does not support.");
        }

        try
        {
            if (dto.DatabaseId is not { } id || dto.Kdf is null || dto.Iterations is not { } iterations)
            {
                throw new FormatException("A field is missing.");
            }

            var wrapped = new WrappedDatabaseKey(
                dto.Kdf,
                iterations,
                Convert.FromBase64String(dto.Salt ?? throw new FormatException("salt")),
                Convert.FromBase64String(dto.Nonce ?? throw new FormatException("nonce")),
                Convert.FromBase64String(dto.Ciphertext ?? throw new FormatException("ciphertext")),
                Convert.FromBase64String(dto.Tag ?? throw new FormatException("tag")));
            return new DatabaseKeyFileContents(id, wrapped);
        }
        catch (FormatException ex)
        {
            throw new DatabaseKeyFileException(path, "The key file is damaged.", ex);
        }
    }

    private sealed class KeyFileDto
    {
        public int Version { get; set; }

        public Guid? DatabaseId { get; set; }

        public string? Kdf { get; set; }

        public int? Iterations { get; set; }

        public string? Salt { get; set; }

        public string? Nonce { get; set; }

        public string? Ciphertext { get; set; }

        public string? Tag { get; set; }
    }
}
