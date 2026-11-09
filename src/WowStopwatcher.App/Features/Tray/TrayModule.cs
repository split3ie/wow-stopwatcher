using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>
/// System tray icon with a menu of the stopwatches. With a tray the app lives there, so closing the main window only hides it.
/// Windows gets the custom popup menu, macOS and Linux the native menu.
/// </summary>
public sealed class TrayModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(new ShellOptions { CloseHidesWindow = true });

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ITrayMenu, WindowsTrayMenu>();
        }
        else
        {
            services.AddSingleton<ITrayMenu, NativeTrayMenu>();
        }

        services.AddSingleton<IAppService, TrayController>();
    }
}
