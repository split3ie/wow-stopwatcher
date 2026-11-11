using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WowStopwatcher.App.Tests.Invoicing;

/// <summary>Creates small DOCX files for tests.</summary>
internal static class DocxTestFactory
{
    public static Run TextRun(string text, bool bold = false)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (bold)
        {
            run.PrependChild(new RunProperties(new Bold()));
        }

        return run;
    }

    public static Paragraph Para(params OpenXmlElement[] children) => new(children);

    public static TableRow Row(params string[] cellTexts) =>
        new(cellTexts.Select(t => new TableCell(Para(TextRun(t)))));

    /// <summary>Writes a DOCX with the given body elements, and optionally a header and a footer paragraph.</summary>
    public static void Create(string path, IEnumerable<OpenXmlElement> body, Paragraph? header = null, Paragraph? footer = null)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var section = new SectionProperties();
        if (header is not null)
        {
            var part = main.AddNewPart<HeaderPart>();
            part.Header = new Header(header);
            part.Header.Save();
            section.Append(new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }

        if (footer is not null)
        {
            var part = main.AddNewPart<FooterPart>();
            part.Footer = new Footer(footer);
            part.Footer.Save();
            section.Append(new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(part) });
        }

        var content = new Body(body);
        content.Append(section);
        main.Document = new Document(content);
        main.Document.Save();
    }

    public static string BodyText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart!.Document!.Body!.InnerText;
    }
}
