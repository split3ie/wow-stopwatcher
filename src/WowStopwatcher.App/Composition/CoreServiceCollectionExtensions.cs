using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Composition;

public static class CoreServiceCollectionExtensions
{
    /// <summary>Registers the clock and the stopwatch engine, which is also available through its narrow interfaces.</summary>
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StopwatchEngine>();
        services.AddSingleton<IStopwatchState>(sp => sp.GetRequiredService<StopwatchEngine>());
        services.AddSingleton<IStopwatchControl>(sp => sp.GetRequiredService<StopwatchEngine>());
        services.AddSingleton<IStopwatchMaintenance>(sp => sp.GetRequiredService<StopwatchEngine>());
        return services;
    }
}
