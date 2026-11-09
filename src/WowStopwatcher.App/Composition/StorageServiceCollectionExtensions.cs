using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.Core;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Composition;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite database and the repositories. The stopwatch repository is wrapped in
    /// <see cref="CapabilityFilteredStopwatchRepository"/>, so everything else sees the stopwatches of the enabled capabilities.
    /// </summary>
    public static IServiceCollection AddStorage(this IServiceCollection services)
    {
        services.AddSingleton(_ => SqliteDatabase.OpenDefault());
        services.AddSingleton<SqliteStopwatchRepository>();
        services.AddSingleton<IStopwatchRepository>(sp => new CapabilityFilteredStopwatchRepository(
            sp.GetRequiredService<SqliteStopwatchRepository>(), sp.GetRequiredService<StopwatchCapabilities>()));
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        return services;
    }
}
