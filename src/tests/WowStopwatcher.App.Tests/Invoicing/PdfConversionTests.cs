using WowStopwatcher.App.Features.Invoicing;
using static WowStopwatcher.App.Tests.Invoicing.DocxTestFactory;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class PdfConversionTests : IDisposable
{
    private const string MinimalPdf =
        "%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 200]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n" +
        "4 0 obj<</Length 44>>stream\nBT /F1 24 Tf 20 100 Td (Hello PDF) Tj ET\nendstream\nendobj\n" +
        "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\ntrailer<</Root 1 0 R/Size 6>>\n%%EOF\n";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public PdfConversionTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static bool IsPng(byte[] bytes) => bytes.Length > 100 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G';

    [Fact]
    public void Preview_renders_page_one_of_a_pdf()
    {
        var pdf = Path.Combine(_folder, "mini.pdf");
        File.WriteAllText(pdf, MinimalPdf);

        var png = new PdfPreviewRenderer().RenderFirstPage(pdf, maxWidth: 300);

        Assert.True(IsPng(png));
    }

    [Fact]
    public async Task LibreOffice_converts_a_docx_when_installed()
    {
        var converter = new LibreOfficePdfConverter();
        if (!converter.IsAvailable)
        {
            return; // LibreOffice is not installed on this machine.
        }

        var docx = Path.Combine(_folder, "in.docx");
        var pdf = Path.Combine(_folder, "out.pdf");
        Create(docx, [Para(TextRun("Hello invoice"))]);

        await converter.ConvertAsync(docx, pdf, CancellationToken.None);

        Assert.True(File.Exists(pdf));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf)[..4]));
        Assert.True(IsPng(new PdfPreviewRenderer().RenderFirstPage(pdf)));
    }

    [Fact]
    public async Task Builder_fills_converts_and_cleans_up_when_installed()
    {
        var selector = new PdfConverterSelector([new WordPdfConverter(), new LibreOfficePdfConverter()]);
        if (!selector.IsAvailable)
        {
            return; // No converter on this machine.
        }

        var template = Path.Combine(_folder, "t.docx");
        Create(template, [Para(TextRun("Hello {{client}}"))]);
        var builder = new InvoicePdfBuilder(new DocxTemplateFiller(), selector);

        var pdf = await builder.BuildAsync(template, new Dictionary<string, string> { ["client"] = "ACME" }, [], CancellationToken.None);

        Assert.True(File.Exists(pdf));
        InvoicePdfBuilder.DeleteTemp(pdf);
        Assert.False(File.Exists(pdf));
    }

    [Fact]
    public async Task Builder_removes_its_temp_folder_on_failure()
    {
        var failing = new PdfConverterSelector([new FailingConverter()]);
        var template = Path.Combine(_folder, "t.docx");
        Create(template, [Para(TextRun("x"))]);
        var builder = new InvoicePdfBuilder(new DocxTemplateFiller(), failing);
        var before = Directory.GetDirectories(Path.GetTempPath(), "wowstopwatcher-invoice-*").Length;

        await Assert.ThrowsAsync<PdfConversionException>(() => builder.BuildAsync(template, new Dictionary<string, string>(), [], CancellationToken.None));

        Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "wowstopwatcher-invoice-*").Length);
    }

    [Fact]
    public async Task Selector_without_a_converter_reports_it()
    {
        var selector = new PdfConverterSelector([]);

        Assert.False(selector.IsAvailable);
        Assert.Null(selector.Name);
        await Assert.ThrowsAsync<PdfConversionException>(() => selector.ConvertAsync("a.docx", "a.pdf", CancellationToken.None));
    }

    private sealed class FailingConverter : IPdfConverter
    {
        public string Name => "Failing";

        public bool IsAvailable => true;

        public Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken) =>
            throw new PdfConversionException(PdfErrorCode.Failed, "failed");
    }
}
