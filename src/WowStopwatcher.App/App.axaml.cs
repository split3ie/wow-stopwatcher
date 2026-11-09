using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;

namespace WowStopwatcher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: the only place that builds and uses the service provider directly.
            var provider = AppComposition.Build(desktop);
            var options = provider.GetRequiredService<ShellOptions>();

            // One locator maps every registered view model to its view, app-wide.
            DataTemplates.Add(provider.GetRequiredService<ViewLocator>());

            // With a tray the app lives there: closing the main window only hides it and Quit shuts down.
            // Without it, closing the main window ends the app.
            desktop.ShutdownMode = options.CloseHidesWindow ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;

            var mainWindow = provider.GetRequiredService<IMainWindowService>();
            var services = provider.GetServices<IAppService>().ToList();
            foreach (var service in services)
            {
                service.Start();
            }

            // On normal quit the services are disposed in reverse order, then running sessions are stopped and saved.
            // After an update restart they stay open and the next start resumes them.
            var control = provider.GetRequiredService<IStopwatchControl>();
            var lifetime = provider.GetRequiredService<IAppLifetime>();
            desktop.Exit += (_, _) =>
            {
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    services[i].Dispose();
                }

                if (!lifetime.KeepsRunningSessions)
                {
                    control.StopAll();
                }
            };

            // "--minimized" (autostart): keep the main window hidden, only the tray icon is visible.
            // Without a tray the window must be shown, otherwise the app could not be reached.
            if (!options.CloseHidesWindow || desktop.Args?.Contains("--minimized") != true)
            {
                mainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
