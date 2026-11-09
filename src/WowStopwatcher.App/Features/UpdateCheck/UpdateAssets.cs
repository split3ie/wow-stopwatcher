using System.Text.Json;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Pure helpers to pick the release asset for a platform and to read a .sha256 file.</summary>
public static class UpdateAssets
{
    /// <summary>The package and checksum of the asset for a platform.</summary>
    public sealed record Package(string FileName, string Url, string? ChecksumUrl);

    /// <summary>Expected asset name, for example "WowStopwatcher-1.2.3-win-x64.zip"; null on an unsupported platform.</summary>
    public static string? AssetName(string version, string os, string arch)
    {
        var extension = os switch
        {
            "win" => "zip",
            "osx" => "zip",
            "linux" => "tar.gz",
            _ => null,
        };
        return extension is null ? null : $"WowStopwatcher-{version}-{os}-{arch}.{extension}";
    }

    /// <summary>Finds the package of the current platform in the "assets" array of a release.</summary>
    public static Package? Select(JsonElement assets, string version, string os, string arch)
    {
        var name = AssetName(version, os, arch);
        if (name is null || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? packageUrl = null;
        string? checksumUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var n) || !asset.TryGetProperty("browser_download_url", out var u))
            {
                continue;
            }

            if (n.GetString() == name)
            {
                packageUrl = u.GetString();
            }
            else if (n.GetString() == name + ".sha256")
            {
                checksumUrl = u.GetString();
            }
        }

        return packageUrl is null ? null : new Package(name, packageUrl, checksumUrl);
    }

    /// <summary>Platform tokens of this machine, or null when it is not supported.</summary>
    public static (string Os, string Arch)? CurrentPlatform()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "osx"
            : null;
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => null,
        };
        return os is null || arch is null ? null : (os, arch);
    }

    /// <summary>Reads the hash from "&lt;hash&gt;  &lt;filename&gt;" (or a bare hash); null if it is not 64 hex characters.</summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first : null;
    }
}
