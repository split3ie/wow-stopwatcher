using Microsoft.Extensions.DependencyInjection;

namespace WowStopwatcher.App.Shell;

public static class ShellOptionsExtensions
{
    /// <summary>
    /// Changes the <see cref="ShellOptions"/> registered by the shell, so several features can each set their own option.
    /// Call it from a feature module (modules register after the shell).
    /// </summary>
    public static IServiceCollection ConfigureShell(this IServiceCollection services, Action<ShellOptions> configure)
    {
        var options = services.Select(d => d.ImplementationInstance).OfType<ShellOptions>().Last();
        configure(options);
        return services;
    }
}
