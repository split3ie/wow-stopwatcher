using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WowStopwatcher.App.Features.Invoicing;
using static WowStopwatcher.App.Tests.Invoicing.DocxTestFactory;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class DocxTemplateTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public DocxTemplateTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathOf(string name) => Path.Combine(_folder, name);

    private static IReadOnlyDictionary<string, string> Dict(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);

    [Fact]
    public void Scanner_finds_markers_in_body_table_header_and_footer()
    {
        var path = PathOf("t.docx");
        Create(
            path,
            [
                Para(TextRun("Bill to {{client}} and {{ client }}")),
                new Table(Row("{{line.date}}", "{{line.note}}")),
            ],
            header: Para(TextRun("Invoice {{invoice.number}}")),
            footer: Para(TextRun("{{company"), TextRun(".name}}")));

        var names = new DocxTemplateScanner().Scan(path);

        Assert.Equal(["client", "line.date", "line.note", "invoice.number", "company.name"], names);
    }

    [Fact]
    public void Filler_replaces_split_marker_and_keeps_formatting_of_first_run()
    {
        var template = PathOf("t.docx");
        Create(template, [Para(TextRun("Bill to: "), TextRun("{{cli", bold: true), TextRun("en"), TextRun("t}} done"))]);

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), Dict(("client", "ACME Ltd")), []);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        var paragraph = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Single();
        Assert.Equal("Bill to: ACME Ltd done", paragraph.InnerText);
        var run = paragraph.Elements<Run>().Single(r => r.InnerText.Contains("ACME"));
        Assert.NotNull(run.RunProperties?.Bold);
    }

    [Fact]
    public void Filler_replaces_markers_in_header_and_footer()
    {
        var template = PathOf("t.docx");
        Create(template, [Para(TextRun("body"))], header: Para(TextRun("Invoice {{invoice.number}}")), footer: Para(TextRun("{{company.name}}")));

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), Dict(("invoice.number", "7"), ("company.name", "Me Inc")), []);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        Assert.Equal("Invoice 7", document.MainDocumentPart!.HeaderParts.Single().Header!.InnerText);
        Assert.Equal("Me Inc", document.MainDocumentPart.FooterParts.Single().Footer!.InnerText);
    }

    [Fact]
    public void Filler_repeats_the_row_for_each_line()
    {
        var template = PathOf("t.docx");
        Create(template, [new Table(Row("Date", "Note"), Row("{{line.date}}", "{{line.note}}"))]);
        IReadOnlyList<IReadOnlyDictionary<string, string>> lines =
        [
            Dict(("line.date", "d1"), ("line.note", "A")),
            Dict(("line.date", "d2"), ("line.note", "B")),
            Dict(("line.date", "d3"), ("line.note", "C")),
        ];

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), NoValues, lines);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        var rows = document.MainDocumentPart!.Document!.Body!.Descendants<TableRow>().Select(r => r.InnerText).ToList();
        Assert.Equal(["DateNote", "d1A", "d2B", "d3C"], rows);
    }

    [Fact]
    public void Filler_removes_the_row_when_there_are_no_lines()
    {
        var template = PathOf("t.docx");
        Create(template, [new Table(Row("Date", "Note"), Row("{{line.date}}", "{{line.note}}"))]);

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), NoValues, []);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        var rows = document.MainDocumentPart!.Document!.Body!.Descendants<TableRow>().Select(r => r.InnerText).ToList();
        Assert.Equal(["DateNote"], rows);
    }

    [Fact]
    public void Filler_leaves_unknown_markers_and_does_not_change_the_template()
    {
        var template = PathOf("t.docx");
        Create(template, [Para(TextRun("{{known}} and {{unknown}}"))]);
        var before = File.ReadAllBytes(template);

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), Dict(("known", "yes")), []);

        Assert.Equal("yes and {{unknown}}", BodyText(PathOf("out.docx")));
        Assert.Equal(before, File.ReadAllBytes(template));
    }

    [Fact]
    public void Filler_handles_runs_with_tabs_breaks_and_hyperlinks()
    {
        var template = PathOf("t.docx");
        var tabbed = new Run(new Text("A\t") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }, new TabChar(), new Break(), new Text("{{x}}"));
        Create(template, [Para(tabbed), Para(new Hyperlink(TextRun("{{y}}")) { Anchor = "a" })]);

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), Dict(("x", "1"), ("y", "2")), []);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        var body = document.MainDocumentPart!.Document!.Body!;
        Assert.Contains("1", body.Elements<Paragraph>().First().InnerText);
        Assert.NotNull(body.Descendants<TabChar>().FirstOrDefault());
        Assert.Equal("2", body.Elements<Paragraph>().Last().InnerText);
    }

    [Fact]
    public void Filler_never_rescans_inserted_values_for_markers()
    {
        var template = PathOf("t.docx");
        Create(template, [Para(TextRun("{{client}}")), new Table(Row("{{line.note}}", "{{client}}"))]);
        IReadOnlyList<IReadOnlyDictionary<string, string>> lines = [Dict(("line.note", "see {{client}} and {{line.note}}"))];

        new DocxTemplateFiller().Fill(template, PathOf("out.docx"), Dict(("client", "ACME {{line.note}}")), lines);

        using var document = WordprocessingDocument.Open(PathOf("out.docx"), false);
        var body = document.MainDocumentPart!.Document!.Body!;
        Assert.Equal("ACME {{line.note}}", body.Elements<Paragraph>().First().InnerText);
        Assert.Equal("see {{client}} and {{line.note}}ACME {{line.note}}", body.Descendants<TableRow>().Single().InnerText);
    }
}
