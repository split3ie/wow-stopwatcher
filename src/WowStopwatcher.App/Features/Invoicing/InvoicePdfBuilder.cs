namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Builds the PDF of an invoice in a temporary folder: fill the template, then convert it.</summary>
public sealed class InvoicePdfBuilder
{
    private readonly DocxTemplateFiller _filler;
    private readonly PdfConverterSelector _converter;

    /// <summary>Creates the builder.</summary>
    public InvoicePdfBuilder(DocxTemplateFiller filler, PdfConverterSelector converter)
    {
        _filler = filler;
        _converter = converter;
    }

    /// <summary>
    /// Returns the path of the PDF in a new temp folder; the caller moves it and deletes the folder (<see cref="DeleteTemp"/>).
    /// On failure the temp folder is removed.
    /// </summary>
    public async Task<string> BuildAsync(
        string templatePath,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<IReadOnlyDictionary<string, string>> lines,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-invoice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var docx = Path.Combine(folder, "invoice.docx");
            var pdf = Path.Combine(folder, "invoice.pdf");
            _filler.Fill(templatePath, docx, values, lines);
            await _converter.ConvertAsync(docx, pdf, cancellationToken);
            return pdf;
        }
        catch
        {
            DeleteTemp(folder);
            throw;
        }
    }

    /// <summary>Deletes the temp folder of a built PDF (or the folder itself); failures are ignored.</summary>
    public static void DeleteTemp(string pdfOrFolder)
    {
        var folder = Directory.Exists(pdfOrFolder) ? pdfOrFolder : Path.GetDirectoryName(pdfOrFolder);
        if (folder is null || !Path.GetFileName(folder).StartsWith("wowstopwatcher-invoice-", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp folder is harmless.
        }
    }
}
