using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Composition;

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the shell services, navigation and the null shortcut service.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        services.AddSingleton(desktop);
        services.AddSingleton<IAppLifetime, DesktopAppLifetime>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IAppVersion, AppVersion>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IFolderLauncher, AvaloniaFolderLauncher>();
        services.AddSingleton<IDatabaseKeyStore>(_ => DatabaseKeyStoreFactory.Create());
        services.AddSingleton<DatabaseRecovery>();
        services.AddSingleton<IStartupPrompts, AvaloniaStartupPrompts>();
        services.AddSingleton<StartupLanguageApplier>();
        services.AddSingleton<IStartupLanguage>(sp => sp.GetRequiredService<StartupLanguageApplier>());

        // Without a feature that supports a PIN the "Protect your data" window has no PIN step.
        services.AddSingleton<IStartupPinSetup, NullStartupPinSetup>();

        // Without a feature that keeps the app alive (the tray), closing the main window quits the app.
        services.AddSingleton(new ShellOptions());

        // Shortcuts are not available unless the Hotkeys feature replaces this.
        services.AddSingleton<IShortcutService, NullShortcutService>();

        services.AddSingleton<MainWindowService>();
        services.AddSingleton<IMainWindowService>(sp => sp.GetRequiredService<MainWindowService>());

        services.AddSingleton<ConfirmDialogViewModel>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<ConfirmDialogViewModel>());

        services.AddSingleton<PromptDialogViewModel>();
        services.AddSingleton<IPromptDialogService>(sp => sp.GetRequiredService<PromptDialogViewModel>());

        // Sensitive actions (export, import, folder change) ask nothing unless a feature replaces this.
        services.AddSingleton<ISensitiveActionGuard, NullSensitiveActionGuard>();

        // No page is locked unless a feature replaces this.
        services.AddSingleton<IPageLock>(NullPageLock.Instance);

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<ViewLocator>();

        return services;
    }

    /// <summary>Registers the shell's own background services. Called after the feature modules, so features such as hotkeys and the tray start first.</summary>
    public static IServiceCollection AddShellBackgroundServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppService, SingleInstanceActivationService>();
        return services;
    }
}
