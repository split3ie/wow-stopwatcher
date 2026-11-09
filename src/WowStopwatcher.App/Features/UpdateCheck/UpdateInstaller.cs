using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Replaces the running single-file app (or macOS bundle) with the downloaded release.</summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    private const string AfterUpdateArgument = "--after-update";
    private const string ExecutableName = "WowStopwatcher";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    private static readonly HttpClient Http = CreateClient();

    private static readonly UnixFileMode UnixExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly IAppVersion _version;

    public UpdateInstaller(IAppVersion version)
    {
        _version = version;
    }

    public bool CanInstall(UpdateCheckResult update) =>
        _version.IsOfficialBuild
        && update.Status == UpdateStatus.Available
        && IsHttps(update.PackageUrl)
        && IsHttps(update.ChecksumUrl)
        && !string.IsNullOrEmpty(update.PackageFileName)
        && TryGetTarget(out _);

    public async Task InstallAsync(UpdateCheckResult update, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!CanInstall(update) || !TryGetTarget(out var target))
        {
            throw new InvalidOperationException("This update can’t be installed automatically.");
        }

        var workDir = Path.Combine(Path.GetTempPath(), "WowStopwatcher-update-" + Guid.NewGuid().ToString("N"));
        // On macOS the new bundle is staged next to the current one so the final move is a rename on one volume.
        var extractDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Path.GetDirectoryName(target)!, ".WowStopwatcher-update-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(workDir, "extracted");
        try
        {
            Directory.CreateDirectory(workDir);
            var packagePath = Path.Combine(workDir, Path.GetFileName(update.PackageFileName!));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DownloadTimeout);

            var checksumText = await Http.GetStringAsync(update.ChecksumUrl!, timeout.Token).ConfigureAwait(false);
            var expected = UpdateAssets.ParseChecksum(checksumText)
                ?? throw new InvalidDataException("The checksum file of the update is not valid.");

            await DownloadAsync(update.PackageUrl!, packagePath, progress, timeout.Token).ConfigureAwait(false);

            string actual;
            await using (var stream = File.OpenRead(packagePath))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, timeout.Token).ConfigureAwait(false));
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The downloaded update is corrupted (checksum mismatch).");
            }

            Directory.CreateDirectory(extractDir);
            await ExtractAsync(packagePath, extractDir, timeout.Token).ConfigureAwait(false);

            if (OperatingSystem.IsWindows())
            {
                ReplaceOnWindows(target, Path.Combine(extractDir, ExecutableName + ".exe"));
            }
            else if (OperatingSystem.IsMacOS())
            {
                ReplaceOnMac(target, Path.Combine(extractDir, ExecutableName + ".app"));
            }
            else if (OperatingSystem.IsLinux())
            {
                ReplaceOnLinux(target, Path.Combine(extractDir, ExecutableName));
            }
            else
            {
                throw new PlatformNotSupportedException("Updates can’t be installed on this system.");
            }

            progress?.Report(1);
        }
        finally
        {
            TryDelete(workDir);
            TryDelete(extractDir);
        }
    }

    public void StartNewInstance()
    {
        if (!TryGetTarget(out var target))
        {
            return;
        }

        var info = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsMacOS())
        {
            info.FileName = "open";
            info.ArgumentList.Add("-n");
            info.ArgumentList.Add(target);
            info.ArgumentList.Add("--args");
            info.ArgumentList.Add(AfterUpdateArgument);
        }
        else
        {
            info.FileName = target;
            info.ArgumentList.Add(AfterUpdateArgument);
        }

        using var process = Process.Start(info);
    }

    /// <summary>
    /// The file or folder that an update replaces: the exe (Windows), the binary (Linux) or the .app bundle (macOS).
    /// False when the running process is not a published WowStopwatcher.
    /// </summary>
    internal static bool TryGetTarget(out string target)
    {
        target = string.Empty;
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var expectedName = OperatingSystem.IsWindows() ? ExecutableName + ".exe" : ExecutableName;
        if (!string.Equals(Path.GetFileName(path), expectedName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!OperatingSystem.IsMacOS())
        {
            target = path;
            return true;
        }

        // .../X.app/Contents/MacOS/WowStopwatcher
        var macOsDir = Path.GetDirectoryName(path);
        var contentsDir = macOsDir is null ? null : Path.GetDirectoryName(macOsDir);
        var bundle = contentsDir is null ? null : Path.GetDirectoryName(contentsDir);
        if (bundle is null
            || Path.GetFileName(macOsDir) != "MacOS"
            || Path.GetFileName(contentsDir) != "Contents"
            || !bundle.EndsWith(".app", StringComparison.Ordinal))
        {
            return false;
        }

        target = bundle;
        return true;
    }

    /// <summary>Leftover of a previous update (renamed old exe or bundle); null when there is none on this platform.</summary>
    internal static string? OldLeftoverPath() =>
        TryGetTarget(out var target) && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            ? target + ".old"
            : null;

    private static async Task DownloadAsync(string url, string path, IProgress<double>? progress, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var destination = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            done += read;
            if (total is > 0)
            {
                progress?.Report((double)done / total.Value);
            }
        }
    }

    private static async Task ExtractAsync(string packagePath, string extractDir, CancellationToken token)
    {
        if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (OperatingSystem.IsMacOS())
            {
                // ditto keeps the bundle's metadata and code signature exactly (the zip was made by ditto).
                await ExtractWithDittoAsync(packagePath, extractDir, token).ConfigureAwait(false);
            }
            else
            {
                ZipFile.ExtractToDirectory(packagePath, extractDir);
            }

            return;
        }

        await using var file = File.OpenRead(packagePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, extractDir, overwriteFiles: false, token).ConfigureAwait(false);
    }

    private static async Task ExtractWithDittoAsync(string zipPath, string extractDir, CancellationToken token)
    {
        var info = new ProcessStartInfo("ditto") { UseShellExecute = false };
        info.ArgumentList.Add("-x");
        info.ArgumentList.Add("-k");
        info.ArgumentList.Add(zipPath);
        info.ArgumentList.Add(extractDir);
        using var process = Process.Start(info) ?? throw new IOException("Could not start ditto to unpack the update.");
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new IOException($"ditto failed to unpack the update (exit code {process.ExitCode}).");
        }
    }

    private static void ReplaceOnWindows(string current, string extracted)
    {
        RequireFile(extracted);
        var old = current + ".old";
        // A running exe can be renamed but not overwritten.
        File.Move(current, old, overwrite: true);
        try
        {
            File.Move(extracted, current);
        }
        catch
        {
            File.Move(old, current, overwrite: true);
            throw;
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void ReplaceOnLinux(string current, string extracted)
    {
        RequireFile(extracted);
        var staged = current + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(extracted, staged);
            File.SetUnixFileMode(staged, UnixExecutableMode);
            File.Move(staged, current, overwrite: true);
        }
        finally
        {
            TryDelete(staged);
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static void ReplaceOnMac(string bundle, string extractedBundle)
    {
        var binary = Path.Combine(extractedBundle, "Contents", "MacOS", ExecutableName);
        RequireFile(binary);
        File.SetUnixFileMode(binary, UnixExecutableMode);

        var old = bundle + ".old";
        if (Directory.Exists(old))
        {
            Directory.Delete(old, recursive: true);
        }

        Directory.Move(bundle, old);
        try
        {
            Directory.Move(extractedBundle, bundle);
        }
        catch
        {
            Directory.Move(old, bundle);
            throw;
        }

        RemoveQuarantine(bundle);
    }

    private static void RemoveQuarantine(string bundle)
    {
        try
        {
            var info = new ProcessStartInfo("xattr") { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-dr");
            info.ArgumentList.Add("com.apple.quarantine");
            info.ArgumentList.Add(bundle);
            using var process = Process.Start(info);
            process?.WaitForExit(10_000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"Could not remove the quarantine attribute: {ex.Message}");
        }
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException("The update package does not contain the expected files.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete '{path}': {ex.Message}");
        }
    }

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WowStopwatcher", "1.0"));
        return client;
    }
}
