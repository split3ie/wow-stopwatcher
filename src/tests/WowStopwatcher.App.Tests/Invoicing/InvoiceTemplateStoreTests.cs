using WowStopwatcher.App.Features.Invoicing;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class InvoiceTemplateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public InvoiceTemplateStoreTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Import_copies_the_file_next_to_the_database_and_resolve_finds_it()
    {
        var store = new InvoiceTemplateStore(new SqliteDatabase(Path.Combine(_folder, "db", "test.db")));
        var source = Path.Combine(_folder, "My Template.docx");
        File.WriteAllText(source, "template");

        var stored = store.Import("company1", source);

        Assert.Equal("My Template.docx", stored.OriginalName);
        Assert.Equal(64, stored.Sha256.Length);
        Assert.Equal($"company1-{stored.Sha256[..8]}.docx", stored.FileName);
        var path = store.Resolve(stored.FileName);
        Assert.Equal(Path.Combine(_folder, "db", "templates", stored.FileName), path);
        Assert.Equal("template", File.ReadAllText(path));
        Assert.Throws<ArgumentException>(() => store.Resolve(@"..\x.docx"));
    }
}
