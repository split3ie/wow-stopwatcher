using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Update check: the "Check for updates" button in the Settings footer.</summary>
public sealed class UpdateCheckModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IUpdateChecker, UpdateChecker>();
        services.AddTransient<ISettingsFooterItem, UpdateCheckFooterViewModel>();
        services.AddSingleton(new ViewMapping(typeof(UpdateCheckFooterViewModel), () => new UpdateCheckFooterView()));
    }
}
