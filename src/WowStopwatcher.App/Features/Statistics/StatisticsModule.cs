using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Statistics;

/// <summary>
/// Statistics: the page with one card per logged day, including restoring hidden stopwatches.
/// Without it the navigation has no Statistics button.
/// </summary>
public sealed class StatisticsModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddTransient<StatisticsViewModel>();
        services.AddSingleton(new PageDescriptor(PageKeys.Statistics, "stats.title", null, 10, typeof(StatisticsViewModel)));
        services.AddSingleton(new ViewMapping(typeof(StatisticsViewModel), () => new StatisticsView()));
    }
}
