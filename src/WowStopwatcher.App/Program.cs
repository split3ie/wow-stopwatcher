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
        if (Instance is null && args.Contains("--after-update"))
        {
            // Started by an update: wait for the old instance to exit (TryAcquire disposes its mutex on failure).
            for (var i = 0; i < 60 && Instance is null; i++)
            {
                Thread.Sleep(250);
                Instance = SingleInstanceGuard.TryAcquire();
            }
        }

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
