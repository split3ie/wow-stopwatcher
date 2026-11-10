using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>
/// Settings: the page for stopwatch editing and the general and footer items other features contribute.
/// Without it the navigation has no Settings button, and the features that live on this page are off too.
/// </summary>
public sealed class SettingsModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddTransient<ISettingsGeneralItem, LanguageSettingsViewModel>();
        services.AddSingleton(new ViewMapping(typeof(LanguageSettingsViewModel), () => new LanguageSettingsView()));
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton(new PageDescriptor(PageKeys.Settings, "settings.title", "IconGear", 100, typeof(SettingsViewModel)));
        services.AddSingleton(new ViewMapping(typeof(SettingsViewModel), () => new SettingsView()));
    }
}
