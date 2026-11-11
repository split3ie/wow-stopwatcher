using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Fills a DOCX template: repeats the table rows that use <c>line.*</c> markers, then replaces the markers.</summary>
public sealed partial class DocxTemplateFiller
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}")]
    private static partial Regex MarkerRegex();

    /// <summary>
    /// Writes the filled copy of <paramref name="templatePath"/> to <paramref name="outputPath"/>; the template is not changed.
    /// A marker without a value is left as it is.
    /// </summary>
    /// <param name="templatePath">The template.</param>
    /// <param name="outputPath">The filled DOCX; overwritten if present.</param>
    /// <param name="values">Invoice-level values by marker name.</param>
    /// <param name="lines">One dictionary per invoice line, by marker name (for example "line.date").</param>
    public void Fill(
        string templatePath,
        string outputPath,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<IReadOnlyDictionary<string, string>> lines)
    {
        File.Copy(templatePath, outputPath, overwrite: true);
        using var document = WordprocessingDocument.Open(outputPath, true);
        var roots = DocxParts.Roots(document).ToList();
        var lineOfRow = new Dictionary<OpenXmlElement, IReadOnlyDictionary<string, string>>();
        foreach (var root in roots)
        {
            RepeatRows(root, lines, lineOfRow);
        }

        // One pass: every paragraph is replaced once, so inserted values are never scanned for markers again.
        foreach (var root in roots)
        {
            foreach (var paragraph in root.Descendants<Paragraph>().ToList())
            {
                var line = paragraph.Ancestors<TableRow>().Select(r => lineOfRow.GetValueOrDefault(r)).FirstOrDefault(l => l is not null);
                ReplaceIn(paragraph, name => Lookup(name, values, line));
            }
        }

        document.MainDocumentPart?.Document?.Save();
        foreach (var header in document.MainDocumentPart?.HeaderParts ?? [])
        {
            header.Header?.Save();
        }

        foreach (var footer in document.MainDocumentPart?.FooterParts ?? [])
        {
            footer.Footer?.Save();
        }
    }

    private static string? Lookup(string name, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string>? line)
    {
        if (name.StartsWith("line.", StringComparison.Ordinal))
        {
            return line is not null && line.TryGetValue(name, out var lineValue) ? lineValue : null;
        }

        return values.TryGetValue(name, out var value) ? value : null;
    }

    private static void RepeatRows(
        OpenXmlElement root,
        IReadOnlyList<IReadOnlyDictionary<string, string>> lines,
        Dictionary<OpenXmlElement, IReadOnlyDictionary<string, string>> lineOfRow)
    {
        // Only the innermost rows with a line marker repeat, so a row that merely contains a repeated nested table does not.
        var rows = root.Descendants<TableRow>()
            .Where(UsesLineMarker)
            .Where(r => !r.Descendants<TableRow>().Any(UsesLineMarker))
            .ToList();

        foreach (var row in rows)
        {
            var table = row.Parent as Table;
            foreach (var line in lines)
            {
                var clone = (TableRow)row.CloneNode(true);
                lineOfRow[clone] = line;
                row.InsertBeforeSelf(clone);
            }

            row.Remove();
            if (table is not null && !table.Elements<TableRow>().Any())
            {
                table.Remove();
            }
        }
    }

    private static bool UsesLineMarker(TableRow row) =>
        row.Descendants<Paragraph>().Any(p =>
            TemplateMarkers.Find(DocxParts.TextOf(p)).Any(n => n.StartsWith("line.", StringComparison.Ordinal)));

    // The replacement goes into the text element where the marker starts, so it keeps that run's formatting.
    private static void ReplaceIn(Paragraph paragraph, Func<string, string?> lookup)
    {
        var texts = DocxParts.TextsOf(paragraph);
        if (texts.Count == 0)
        {
            return;
        }

        var all = string.Concat(texts.Select(t => t.Text));
        var matches = MarkerRegex().Matches(all)
            .Select(m => (Match: m, Value: lookup(m.Groups[1].Value)))
            .Where(m => m.Value is not null)
            .Reverse()
            .ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var parts = texts.Select(t => t.Text).ToArray();
        foreach (var (match, value) in matches)
        {
            int start = match.Index, end = match.Index + match.Length, pos = 0;
            var placed = false;
            for (var i = 0; i < parts.Length; i++)
            {
                var from = pos;
                var to = pos + parts[i].Length;
                pos = to;
                if (to <= start || from >= end)
                {
                    continue;
                }

                var before = parts[i][..Math.Max(0, start - from)];
                var after = parts[i][Math.Min(parts[i].Length, end - from)..];
                parts[i] = before + (placed ? string.Empty : value!) + after;
                placed = true;
            }
        }

        for (var i = 0; i < texts.Count; i++)
        {
            if (texts[i].Text != parts[i])
            {
                texts[i].Text = parts[i];
                texts[i].Space = SpaceProcessingModeValues.Preserve;
            }
        }
    }
}
