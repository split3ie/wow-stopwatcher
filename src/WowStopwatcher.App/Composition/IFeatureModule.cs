using Microsoft.Extensions.DependencyInjection;

namespace WowStopwatcher.App.Composition;

/// <summary>A pluggable feature: registers its own services (background services, pages, settings) with the container.</summary>
public interface IFeatureModule
{
    void Register(IServiceCollection services);
}
