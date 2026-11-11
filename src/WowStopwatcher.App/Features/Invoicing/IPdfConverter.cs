namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Converts a DOCX file to PDF with an installed office program.</summary>
public interface IPdfConverter
{
    /// <summary>Display name, for example "Microsoft Word".</summary>
    string Name { get; }

    /// <summary>True when the program is installed and can be used now.</summary>
    bool IsAvailable { get; }

    /// <summary>Converts <paramref name="docxPath"/> to <paramref name="outputPdfPath"/>.</summary>
    /// <exception cref="PdfConversionException">The conversion failed.</exception>
    Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken);
}
