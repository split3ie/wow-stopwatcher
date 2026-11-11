using PDFtoImage;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Renders the first page of a PDF as a PNG.</summary>
public sealed class PdfPreviewRenderer
{
    /// <summary>The first page of <paramref name="pdfPath"/> as PNG bytes, at most <paramref name="maxWidth"/> pixels wide.</summary>
    public byte[] RenderFirstPage(string pdfPath, int maxWidth = 900)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            using var pdf = File.OpenRead(pdfPath);
            using var png = new MemoryStream();
            var options = new RenderOptions(Width: maxWidth);
            Conversion.SavePng(png, pdf, page: 0, options: options);
            return png.ToArray();
        }

        throw new PlatformNotSupportedException("PDF preview works on Windows, Linux and macOS.");
    }
}
