using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;
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
        services.AddSingleton<DatabaseLocation>();
        services.AddSingleton(sp => new DatabaseTransfer(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<DatabaseStartup>();
        // The startup code runs DatabaseStartup before this is first resolved, so a pending move or import is applied first.
        services.AddSingleton(sp => new SqliteDatabase(sp.GetRequiredService<DatabaseLocation>().ResolveDatabasePath()));
        services.AddSingleton(sp => new SqliteStopwatchRepository(
            sp.GetRequiredService<SqliteDatabase>(), sp.GetRequiredService<TimeProvider>(), () => Loc.Current.Code));
        services.AddSingleton<IStopwatchRepository>(sp => new CapabilityFilteredStopwatchRepository(
            sp.GetRequiredService<SqliteStopwatchRepository>(), sp.GetRequiredService<StopwatchCapabilities>()));
        services.AddSingleton<ISessionRepository, SqliteSessionRepository>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<ICompanyRepository>(sp => new SqliteCompanyRepository(
            sp.GetRequiredService<SqliteDatabase>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IInvoiceRepository, SqliteInvoiceRepository>();
        return services;
    }
}
