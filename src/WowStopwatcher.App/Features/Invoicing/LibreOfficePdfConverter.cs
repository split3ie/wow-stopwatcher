using System.Diagnostics;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Converts with a headless LibreOffice, using a throwaway profile so a running LibreOffice is not disturbed.</summary>
public sealed class LibreOfficePdfConverter : IPdfConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public string Name => "LibreOffice";

    /// <inheritdoc />
    public bool IsAvailable => FindExecutable() is not null;

    /// <inheritdoc />
    public async Task ConvertAsync(string docxPath, string outputPdfPath, CancellationToken cancellationToken)
    {
        var executable = FindExecutable() ?? throw new PdfConversionException(PdfErrorCode.NotAvailable, "LibreOffice is not available.");
        var work = Path.Combine(Path.GetTempPath(), "wowstopwatcher-lo-" + Guid.NewGuid().ToString("N"));
        var profileDir = Path.Combine(work, "profile");
        var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(outDir);
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
            {
                "-env:UserInstallation=" + new Uri(profileDir + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/'),
                "--headless",
                "--convert-to", "pdf",
                "--outdir", outDir,
                docxPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new PdfConversionException(PdfErrorCode.Failed, "LibreOffice could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new PdfConversionException(PdfErrorCode.Timeout, "LibreOffice took longer than 60 seconds.");
            }

            await Task.WhenAll(output, error);
            var produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");
            if (process.ExitCode != 0)
            {
                throw new PdfConversionException(PdfErrorCode.Failed, $"LibreOffice exit code {process.ExitCode}. {error.Result}".Trim());
            }

            if (!File.Exists(produced))
            {
                throw new PdfConversionException(PdfErrorCode.OutputMissing, "LibreOffice produced no PDF.");
            }

            File.Move(produced, outputPdfPath, overwrite: true);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static string? FindExecutable()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            })
            {
                if (folder.Length > 0)
                {
                    candidates.Add(Path.Combine(folder, "LibreOffice", "program", "soffice.exe"));
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/LibreOffice.app/Contents/MacOS/soffice");
        }
        else
        {
            candidates.AddRange(["/usr/bin/soffice", "/usr/bin/libreoffice", "/usr/local/bin/soffice", "/snap/bin/libreoffice", "/opt/libreoffice/program/soffice"]);
        }

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
        {
            return found;
        }

        var names = OperatingSystem.IsWindows() ? new[] { "soffice.exe" } : new[] { "soffice", "libreoffice" };
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(folder => names.Select(name => SafeCombine(folder, name)))
            .FirstOrDefault(path => path is not null && File.Exists(path));
    }

    private static string? SafeCombine(string folder, string name)
    {
        try
        {
            return Path.Combine(folder.Trim('"'), name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp folder is harmless.
        }
    }
}
