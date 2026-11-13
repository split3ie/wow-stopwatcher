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
        services.AddSingleton<DatabaseUnlock>();
        services.AddSingleton<DatabaseKeyHolder>();
        services.AddSingleton<IDatabaseKeyProvider>(sp => sp.GetRequiredService<DatabaseKeyHolder>());
        // The startup code runs DatabaseUnlock before this is first resolved, so a pending move or import is applied first
        // and the key of the database is known (DatabaseKeyHolder). Without encryption the database is opened plain, without a key.
        services.AddSingleton(sp =>
        {
            var path = sp.GetRequiredService<DatabaseLocation>().ResolveDatabasePath();
            if (!sp.GetRequiredService<SecurityCapabilities>().Encryption)
            {
                return new SqliteDatabase(path);
            }

            return new SqliteDatabase(
                path,
                sp.GetRequiredService<IDatabaseKeyProvider>().Key
                    ?? throw new InvalidOperationException("The database is opened before it was unlocked."));
        });
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
