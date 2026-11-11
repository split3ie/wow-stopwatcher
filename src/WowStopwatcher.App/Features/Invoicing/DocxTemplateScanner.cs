using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Finds the <c>{{name}}</c> markers a DOCX template uses.</summary>
public sealed class DocxTemplateScanner
{
    /// <summary>The distinct marker names in the body, tables, headers, footers and text boxes, in order of first appearance.</summary>
    public IReadOnlyList<string> Scan(string templatePath)
    {
        using var document = WordprocessingDocument.Open(templatePath, false);
        var names = new List<string>();
        foreach (var root in DocxParts.Roots(document))
        {
            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                names.AddRange(TemplateMarkers.Find(DocxParts.TextOf(paragraph)));
            }
        }

        return names.Distinct().ToList();
    }
}
