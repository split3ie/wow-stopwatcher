using Avalonia;
using WowStopwatcher.App.Services;

namespace WowStopwatcher.App;

internal static class Program
{
    /// <summary>Set while this process is the single running instance.</summary>
    internal static SingleInstanceGuard? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Instance = SingleInstanceGuard.TryAcquire();
        if (Instance is null)
        {
            // Another instance runs: ask it to show its window (not for the silent autostart launch) and exit.
            if (!args.Contains("--minimized"))
            {
                SingleInstanceGuard.SignalExisting();
            }

            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
