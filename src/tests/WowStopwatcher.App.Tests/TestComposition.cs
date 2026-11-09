using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

/// <summary>Builds the app's registrations without starting Avalonia and without the owner's real database.</summary>
internal static class TestComposition
{
    public static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        AppComposition.ConfigureServices(services, new ClassicDesktopStyleApplicationLifetime());

        // Safeguard: replace the real-database registration (the last registration wins). Never resolved by these tests.
        var tempPath = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"), "unused.db");
        services.AddSingleton(_ => new SqliteDatabase(tempPath));
        return services;
    }
}
