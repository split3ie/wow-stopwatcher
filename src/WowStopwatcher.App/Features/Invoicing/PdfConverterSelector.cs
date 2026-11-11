namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Picks the converter to use (Word first, else LibreOffice) and runs conversions one at a time.</summary>
public sealed class PdfConverterSelector
{
    private readonly IReadOnlyList<IPdfConverter> _converters;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the selector; converters are tried in the given order.</summary>
    public PdfConverterSelector(IEnumerable<IPdfConverter> converters)
    {
        _converters = converters.ToList();
    }

    /// <summary>The converter that would be used now; null when none is installed. Checked on every call.</summary>
    public IPdfConverter? Current => _converters.FirstOrDefault(c => c.IsAvailable);

    /// <summary>True when a converter is available.</summary>
    public bool IsAvailable => Current is not null;

    /// <summary>The name of the converter in use, for display; null when none is available.</summary>
    public string? Name => Current?.Name;

    /// <summary>Converts with the current converter, waiting for any conversion already running.</summary>
    /// <exception cref="PdfConversionException">No converter is available or the conversion failed.</exception>
    public async Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var converter = Current ?? throw new PdfConversionException(PdfErrorCode.NotAvailable, "Neither Microsoft Word nor LibreOffice is installed.");
            await converter.ConvertAsync(docxPath, outputPdfPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
