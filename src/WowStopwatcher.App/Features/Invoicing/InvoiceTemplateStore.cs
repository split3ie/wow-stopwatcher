using System.Security.Cryptography;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Keeps app-owned copies of the DOCX templates in <c>templates/</c> next to the database.</summary>
public sealed class InvoiceTemplateStore
{
    private readonly string _folder;

    /// <summary>Creates the store; the folder is <c>templates</c> next to the database file.</summary>
    public InvoiceTemplateStore(SqliteDatabase database)
    {
        var databaseFolder = Path.GetDirectoryName(database.Path) ?? throw new InvalidOperationException("The database path has no folder.");
        _folder = Path.Combine(databaseFolder, "templates");
    }

    /// <summary>Copies <paramref name="sourcePath"/> as <c>&lt;companyId&gt;-&lt;first 8 of sha256&gt;.docx</c>; an identical copy is reused.</summary>
    public StoredTemplate Import(string companyId, string sourcePath)
    {
        Directory.CreateDirectory(_folder);
        string hash;
        using (var source = File.OpenRead(sourcePath))
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(source));
        }

        var fileName = $"{companyId}-{hash[..8]}.docx";
        var target = Path.Combine(_folder, fileName);
        if (!File.Exists(target))
        {
            File.Copy(sourcePath, target);
        }

        return new StoredTemplate(fileName, Path.GetFileName(sourcePath), hash);
    }

    /// <summary>The full path of a stored template; the file may be missing.</summary>
    public string Resolve(string fileName)
    {
        // Both separators on every system: on Linux "..\x.docx" is a valid file name, but not a template name.
        if (fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("A template is a file name without folders.", nameof(fileName));
        }

        return Path.Combine(_folder, fileName);
    }
}
