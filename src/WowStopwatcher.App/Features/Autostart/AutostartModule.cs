using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Autostart;

/// <summary>
/// Autostart: the "Run with computer start" card in Settings. Registers the implementation for the current
/// operating system; on an unsupported system (or when the executable path is unknown) it registers nothing,
/// so the card does not appear.
/// </summary>
public sealed class AutostartModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        var autostart = Create();
        if (autostart is null)
        {
            return;
        }

        services.AddSingleton(autostart);
        services.AddTransient<ISettingsGeneralItem, AutostartSettingsViewModel>();
        services.AddSingleton(new ViewMapping(typeof(AutostartSettingsViewModel), () => new AutostartSettingsView()));
    }

    private static IAutostartService? Create()
    {
        if (!AutostartCommand.HasProcessPath)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsAutostart();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacAutostart();
        }

        return OperatingSystem.IsLinux() ? new LinuxAutostart() : null;
    }
}
