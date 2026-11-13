using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.Invoicing;

namespace WowStopwatcher.App.Tests.Invoicing;

public sealed class TempFolderCleanupServiceTests : IDisposable
{
    private static readonly string Guid1 = new('a', 32);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));

    public TempFolderCleanupServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Make(string name, TimeSpan age)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "leftover.txt"), "x");
        Directory.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime - age);
        return path;
    }

    [Fact]
    public void Deletes_old_invoice_and_libreoffice_folders()
    {
        var invoice = Make("wowstopwatcher-invoice-" + Guid1, TimeSpan.FromHours(2));
        var lo = Make("wowstopwatcher-lo-" + Guid1, TimeSpan.FromDays(3));

        var removed = new TempFolderCleanupService(_root, _time).CleanUp();

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(invoice));
        Assert.False(Directory.Exists(lo));
    }

    [Fact]
    public void Keeps_folders_younger_than_an_hour()
    {
        var recent = Make("wowstopwatcher-invoice-" + Guid1, TimeSpan.FromMinutes(59));

        Assert.Equal(0, new TempFolderCleanupService(_root, _time).CleanUp());
        Assert.True(Directory.Exists(recent));
    }

    [Theory]
    [InlineData("wowstopwatcher-invoice-")]
    [InlineData("wowstopwatcher-invoice-aaaa")]
    [InlineData("wowstopwatcher-invoice-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("wowstopwatcher-invoice-gggggggggggggggggggggggggggggggg")]
    [InlineData("wowstopwatcher-invoice-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1")]
    [InlineData("xwowstopwatcher-lo-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("wowstopwatcher-tests-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("other")]
    public void Ignores_folders_whose_name_does_not_match_exactly(string name)
    {
        var folder = Make(name, TimeSpan.FromDays(10));

        Assert.Equal(0, new TempFolderCleanupService(_root, _time).CleanUp());
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Ignores_files_and_a_missing_root()
    {
        var file = Path.Combine(_root, "wowstopwatcher-invoice-" + Guid1);
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, _time.GetUtcNow().UtcDateTime - TimeSpan.FromDays(1));

        Assert.Equal(0, new TempFolderCleanupService(_root, _time).CleanUp());
        Assert.True(File.Exists(file));
        Assert.Equal(0, new TempFolderCleanupService(Path.Combine(_root, "missing"), _time).CleanUp());
    }
}
