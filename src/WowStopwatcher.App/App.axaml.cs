using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.App.Views;
using WowStopwatcher.Core;
using WowStopwatcher.Data;

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

            // The database is inspected read-only first. If it cannot be used safely, show an error and touch nothing:
            // no services start and no exit handler (which would write) is registered.
            SqliteDatabase database;
            try
            {
                database = provider.GetRequiredService<SqliteDatabase>();
            }
            catch (DatabaseOpenException ex)
            {
                StartupLanguage.Apply((string?)null);
                var errorWindow = new DatabaseErrorWindow(new DatabaseErrorViewModel(ex));
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = errorWindow;
                errorWindow.Show();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            new DatabaseBackupCleaner(database.Path, provider.GetRequiredService<TimeProvider>())
                .DeleteOlderThan(DatabaseBackupCleaner.DefaultMaxAge);

            // The language must be set before any window or view model is created.
            StartupLanguage.Apply(provider.GetRequiredService<ISettingsRepository>());

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
