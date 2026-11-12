using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

/// <summary>Builds the app's registrations without starting Avalonia and without the owner's real database.</summary>
internal static class TestComposition
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> TempFolders = [];

    static TestComposition() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var folder in TempFolders)
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // Best effort: a leftover temp folder is harmless.
                }
            }
        };

    public static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        AppComposition.ConfigureServices(services, new ClassicDesktopStyleApplicationLifetime());

        // Safeguard: replace the real-database registration (the last registration wins). Never resolved by these tests.
        var tempFolder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));
        TempFolders.Add(tempFolder);
        var tempPath = Path.Combine(tempFolder, "unused.db");
        services.AddSingleton(_ => new SqliteDatabase(tempPath));
        return services;
    }
}
