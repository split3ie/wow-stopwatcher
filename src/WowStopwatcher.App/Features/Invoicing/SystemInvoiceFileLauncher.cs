using System.ComponentModel;
using System.Diagnostics;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary><see cref="IInvoiceFileLauncher"/> with the OS shell: ShellExecute on Windows, <c>open</c> on macOS, <c>xdg-open</c> on Linux.</summary>
public sealed class SystemInvoiceFileLauncher : IInvoiceFileLauncher
{
    public bool OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        return Start(Tool(OperatingSystem.IsMacOS() ? "open" : "xdg-open", path));
    }

    public bool ShowInFolder(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"" });
        }

        if (OperatingSystem.IsMacOS())
        {
            return Start(Tool("open", "-R", path));
        }

        return Path.GetDirectoryName(path) is { } folder && Start(Tool("xdg-open", folder));
    }

    private static ProcessStartInfo Tool(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
