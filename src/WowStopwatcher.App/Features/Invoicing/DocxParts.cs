using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Helpers shared by the DOCX scanner and filler.</summary>
internal static class DocxParts
{
    /// <summary>The roots that can contain markers: the body, headers and footers.</summary>
    public static IEnumerable<OpenXmlElement> Roots(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        if (main?.Document?.Body is { } body)
        {
            yield return body;
        }

        foreach (var header in main?.HeaderParts ?? [])
        {
            if (header.Header is { } root)
            {
                yield return root;
            }
        }

        foreach (var footer in main?.FooterParts ?? [])
        {
            if (footer.Footer is { } root)
            {
                yield return root;
            }
        }
    }

    /// <summary>
    /// The text elements of a paragraph in order: those of all its runs (also inside hyperlinks and content controls),
    /// without the runs of paragraphs nested in it (text boxes).
    /// </summary>
    public static List<Text> TextsOf(Paragraph paragraph) =>
        paragraph.Descendants<Run>()
            .Where(run => run.Ancestors<Paragraph>().FirstOrDefault() == paragraph)
            .SelectMany(run => run.Elements<Text>())
            .ToList();

    public static string TextOf(Paragraph paragraph) => string.Concat(TextsOf(paragraph).Select(t => t.Text));
}
