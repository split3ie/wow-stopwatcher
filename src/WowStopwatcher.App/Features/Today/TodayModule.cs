using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.Today;

/// <summary>
/// Today: the page with the timeline, the stopwatch rows, the total and the goals.
/// Without it the navigation has no Today button.
/// </summary>
public sealed class TodayModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<TodayViewModel>();
        services.AddSingleton<IWindowVisibilityAware>(sp => sp.GetRequiredService<TodayViewModel>());
        services.AddSingleton(new PageDescriptor(PageKeys.Today, "today.title", null, 0, typeof(TodayViewModel)));
        services.AddSingleton(new ViewMapping(typeof(TodayViewModel), () => new TodayView()));
    }
}
