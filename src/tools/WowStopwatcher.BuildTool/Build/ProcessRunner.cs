using System.Diagnostics;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>Runs an external process, streams its output line by line and kills the process tree on cancel.</summary>
public static class ProcessRunner
{
    /// <summary>Runs the process and returns its exit code. On cancellation the process tree is killed.</summary>
    public static async Task<int> RunAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory,
        Action<string> onLine, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellation);
            process.WaitForExit(); // flush the remaining output events
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        return process.ExitCode;
    }
}
