using System.Diagnostics;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>Closes a running app from the output folder and moves its old file aside before publishing.</summary>
public static class OutputPreparer
{
    private const string AppName = "WowStopwatcher";

    /// <summary>Returns false when the old file could not be moved away (the target must not be published).</summary>
    public static async Task<bool> PrepareAsync(
        string outputDir, string rid, Action<string> log, CancellationToken token)
    {
        var windows = rid.StartsWith("win-", StringComparison.Ordinal);
        var fileName = windows ? AppName + ".exe" : AppName;
        var oldName = windows ? AppName + ".old.exe" : AppName + ".old";
        var path = Path.Combine(outputDir, fileName);
        if (!File.Exists(path))
        {
            return true;
        }

        await CloseRunningAsync(path, log, token);

        var oldPath = Path.Combine(outputDir, oldName);
        try
        {
            File.Move(path, oldPath, overwrite: true);
            log($"Renamed {fileName} to {oldName}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Cannot rename {fileName} to {oldName}: {ex.Message}");
            log("Close the program using it and build again.");
            return false;
        }
    }

    private static async Task CloseRunningAsync(string path, Action<string> log, CancellationToken token)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullPath = Path.GetFullPath(path);
        foreach (var process in Process.GetProcessesByName(AppName))
        {
            using (process)
            {
                try
                {
                    var module = process.MainModule?.FileName;
                    if (module is null || !string.Equals(Path.GetFullPath(module), fullPath, comparison))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    continue;
                }

                try
                {
                    log($"Closing running WowStopwatcher (pid {process.Id})...");
                    process.CloseMainWindow();
                    if (!await WaitAsync(process, TimeSpan.FromSeconds(5), token))
                    {
                        log("Did not exit, killing it.");
                        process.Kill();
                        await WaitAsync(process, TimeSpan.FromSeconds(5), token);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process already exited or cannot be controlled; the rename below reports a real problem.
                }
            }
        }
    }

    private static async Task<bool> WaitAsync(Process process, TimeSpan timeout, CancellationToken token)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
    }
}
