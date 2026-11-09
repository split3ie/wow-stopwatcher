#if FEATURE_UPDATE_CHECK
using System.Text.Json;
using WowStopwatcher.App.Features.UpdateCheck;

namespace WowStopwatcher.App.Tests;

public class UpdateAssetsTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static JsonElement Assets() => JsonDocument.Parse("""
        [
          { "name": "WowStopwatcher-1.2.3-win-x64.zip", "browser_download_url": "https://x/win.zip" },
          { "name": "WowStopwatcher-1.2.3-win-x64.zip.sha256", "browser_download_url": "https://x/win.zip.sha256" },
          { "name": "WowStopwatcher-1.2.3-linux-x64.tar.gz", "browser_download_url": "https://x/linux.tar.gz" },
          { "name": "WowStopwatcher-1.2.3-osx-arm64.zip", "browser_download_url": "https://x/mac.zip" },
          { "name": "WowStopwatcher-1.2.3-osx-arm64.zip.sha256", "browser_download_url": "https://x/mac.zip.sha256" }
        ]
        """).RootElement.Clone();

    [Theory]
    [InlineData("win", "x64", "WowStopwatcher-1.2.3-win-x64.zip")]
    [InlineData("linux", "x64", "WowStopwatcher-1.2.3-linux-x64.tar.gz")]
    [InlineData("osx", "arm64", "WowStopwatcher-1.2.3-osx-arm64.zip")]
    public void AssetName_follows_the_release_naming(string os, string arch, string expected) =>
        Assert.Equal(expected, UpdateAssets.AssetName("1.2.3", os, arch));

    [Fact]
    public void Select_returns_package_and_checksum()
    {
        var package = UpdateAssets.Select(Assets(), "1.2.3", "win", "x64");

        Assert.NotNull(package);
        Assert.Equal("https://x/win.zip", package.Url);
        Assert.Equal("https://x/win.zip.sha256", package.ChecksumUrl);
    }

    [Fact]
    public void Select_without_checksum_asset_has_no_checksum_url()
    {
        var package = UpdateAssets.Select(Assets(), "1.2.3", "linux", "x64");

        Assert.NotNull(package);
        Assert.Null(package.ChecksumUrl);
    }

    [Fact]
    public void Select_returns_null_when_platform_has_no_asset() =>
        Assert.Null(UpdateAssets.Select(Assets(), "1.2.3", "osx", "x64"));

    [Fact]
    public void Select_returns_null_when_assets_are_not_an_array() =>
        Assert.Null(UpdateAssets.Select(JsonDocument.Parse("{}").RootElement, "1.2.3", "win", "x64"));

    [Theory]
    [InlineData(Hash + "  WowStopwatcher-1.2.3-win-x64.zip")]
    [InlineData(Hash + " *file.zip\n")]
    [InlineData(Hash)]
    public void ParseChecksum_reads_the_hash(string text) => Assert.Equal(Hash, UpdateAssets.ParseChecksum(text));

    [Theory]
    [InlineData("")]
    [InlineData("not a hash  file.zip")]
    [InlineData("abc123  file.zip")]
    public void ParseChecksum_rejects_invalid_text(string text) => Assert.Null(UpdateAssets.ParseChecksum(text));
}
#endif
