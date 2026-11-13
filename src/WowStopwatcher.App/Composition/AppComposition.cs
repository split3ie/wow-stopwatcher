using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace WowStopwatcher.App.Composition;

/// <summary>Builds the service provider: core, storage, shell, then every feature module.</summary>
public static class AppComposition
{
    public static ServiceProvider Build(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var services = new ServiceCollection();
        ConfigureServices(services, desktop);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>Adds all registrations without building the provider (also used by the composition tests).</summary>
    public static void ConfigureServices(IServiceCollection services, IClassicDesktopStyleApplicationLifetime desktop)
    {
        services.AddSingleton(FeatureModules.Capabilities);
        services.AddSingleton(FeatureModules.Security);
        services.AddCore();
        services.AddStorage();
        services.AddShell(desktop);

        foreach (var module in FeatureModules.All)
        {
            module.Register(services);
        }

        services.AddShellBackgroundServices();
    }
}
