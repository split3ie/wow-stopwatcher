using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Hotkeys;

/// <summary>
/// Global keyboard shortcuts: the hotkey service (a background service) and the shortcut service Settings uses to show
/// the shortcut fields. Registers the backend of the current operating system, or the unsupported one.
/// </summary>
public sealed class HotkeysModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IHotkeyBackend>(_ => CreateBackend());
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<IShortcutService>(sp => sp.GetRequiredService<GlobalHotkeyService>());
        services.AddSingleton<IAppService>(sp => sp.GetRequiredService<GlobalHotkeyService>());
    }

    private static IHotkeyBackend CreateBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsHotkeyBackend();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacHotkeyBackend();
        }

        if (OperatingSystem.IsLinux())
        {
            return (IHotkeyBackend?)X11HotkeyBackend.TryCreate() ?? new UnsupportedHotkeyBackend();
        }

        return new UnsupportedHotkeyBackend();
    }
}
