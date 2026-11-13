namespace WowStopwatcher.Data;

/// <summary>Tells a plain SQLite file from an encrypted one by reading the first 16 bytes. The file is not modified.</summary>
public static class DatabaseFormatDetector
{
    private static readonly byte[] PlainHeader = "SQLite format 3\0"u8.ToArray();

    /// <summary>Detects the format of the file at <paramref name="path"/>. Throws <see cref="IOException"/> when it cannot be read.</summary>
    public static DatabaseFileFormat Detect(string path)
    {
        if (!File.Exists(path))
        {
            return DatabaseFileFormat.Missing;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length == 0)
        {
            return DatabaseFileFormat.Missing;
        }

        var header = new byte[PlainHeader.Length];
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return read == header.Length && header.AsSpan().SequenceEqual(PlainHeader) ? DatabaseFileFormat.Plain : DatabaseFileFormat.Encrypted;
    }
}
