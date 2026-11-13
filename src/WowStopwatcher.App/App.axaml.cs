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
using WowStopwatcher.Core.Localization;
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

            // The startup windows (protect data, recovery code) can show before the main window.
            // Closing one of them must not end the app by itself, so the shutdown mode is set again once startup is done.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start(desktop, provider);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Unlocks the database (possibly through startup windows), then starts the app. When no window is needed this runs to the end
    /// before it returns, like the previous synchronous startup.
    /// </summary>
    private async void Start(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider provider)
    {
        // The database is inspected read-only first. If it cannot be used safely, show an error and touch nothing:
        // no services start and no exit handler (which would write) is registered.
        // First the pending move/import step and the location checks run, then the database is unlocked and opened.
        SqliteDatabase database;
        UnlockResult unlock;
        try
        {
            var result = await provider.GetRequiredService<DatabaseUnlock>().RunAsync();
            if (result is null)
            {
                // A startup window was closed or Quit was chosen: nothing was changed.
                desktop.Shutdown();
                return;
            }

            unlock = result;
            if (unlock.Key is { } key && unlock.DatabaseId is { } databaseId)
            {
                provider.GetRequiredService<DatabaseKeyHolder>().Set(key, databaseId);
            }

            database = provider.GetRequiredService<SqliteDatabase>();
        }
        catch (DatabaseOpenException ex)
        {
            // Keep the language a startup window already used; otherwise follow the system language.
            var language = provider.GetRequiredService<StartupLanguageApplier>();
            if (!language.HasApplied)
            {
                language.Apply(null);
            }

            var errorWindow = new DatabaseErrorWindow(new DatabaseErrorViewModel(ex, provider.GetRequiredService<SecurityCapabilities>().Encryption), provider.GetRequiredService<DatabaseRecovery>());
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = errorWindow;
            errorWindow.Show();
            return;
        }

        new DatabaseBackupCleaner(database.Path, provider.GetRequiredService<TimeProvider>())
            .DeleteOlderThan(DatabaseBackupCleaner.DefaultMaxAge);

        // The language must be set before any window or view model is created.
        StartupLanguage.Apply(provider.GetRequiredService<ISettingsRepository>());

        // The PIN chosen in the "Protect your data" window is handed over now that settings can be used.
        if (unlock.PinChoice is { } pinChoice)
        {
            provider.GetRequiredService<IStartupPinSetup>().Apply(pinChoice.Pin);
        }

        var options = provider.GetRequiredService<ShellOptions>();

        // One locator maps every registered view model to its view, app-wide.
        DataTemplates.Add(provider.GetRequiredService<ViewLocator>());

        // With a tray the app lives there: closing the main window only hides it and Quit shuts down.
        // Without it, closing the main window ends the app.
        desktop.ShutdownMode = options.CloseHidesWindow ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;

        var mainWindow = provider.GetRequiredService<IMainWindowService>();
        // File pickers and the folder launcher find their window through desktop.MainWindow.
        desktop.MainWindow = mainWindow.Owner;
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
        // The startup windows above ignore it: they need input.
        if (!options.CloseHidesWindow || desktop.Args?.Contains("--minimized") != true)
        {
            mainWindow.Show();
        }

        // The result of a move, replace or import that ran at startup is shown once.
        if (unlock.PendingOutcome is not null)
        {
            var (title, body) = TransferMessages.DescribeOutcome(unlock.PendingOutcome);
            provider.GetRequiredService<IDialogService>().Show(title, body, Loc.T("common.ok"), () => { });
        }
    }
}
