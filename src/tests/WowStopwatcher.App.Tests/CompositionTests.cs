using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests;

public class CompositionTests
{
    [Fact]
    public void Container_passes_ValidateOnBuild_and_ValidateScopes()
    {
        using var provider = TestComposition.CreateServices()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider);
    }

    [Fact]
    public void Every_page_view_model_has_a_view_mapping()
    {
        var services = TestComposition.CreateServices();
        var mapped = MappedViewModels(services);

        foreach (var page in Instances<PageDescriptor>(services))
        {
            Assert.True(mapped.Contains(page.ViewModelType), $"Page '{page.Key}' ({page.ViewModelType.Name}) has no ViewMapping.");
        }
    }

    [Fact]
    public void Every_settings_item_has_a_view_mapping()
    {
        var services = TestComposition.CreateServices();
        var mapped = MappedViewModels(services);

        var items = services
            .Where(d => d.ServiceType == typeof(ISettingsGeneralItem) || d.ServiceType == typeof(ISettingsFooterItem))
            .Select(d => d.ImplementationType ?? d.ImplementationInstance?.GetType())
            .ToList();

        Assert.All(items, t =>
        {
            Assert.NotNull(t); // a factory registration would hide the type; register settings items by type
            Assert.True(mapped.Contains(t), $"Settings item {t.Name} has no ViewMapping.");
        });
    }

    [Fact]
    public void Page_keys_are_unique()
    {
        var keys = Instances<PageDescriptor>(TestComposition.CreateServices()).Select(p => p.Key).ToList();

        Assert.NotEmpty(keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Feature_modules_match_the_enabled_features()
    {
        var actual = FeatureModules.All.Select(m => m.GetType().Name).Order().ToList();
        var expected = new List<string>();
#if FEATURE_TODAY
        expected.Add("TodayModule");
#endif
#if FEATURE_SETTINGS
        expected.Add("SettingsModule");
#endif
#if FEATURE_HOTKEYS
        expected.Add("HotkeysModule");
#endif
#if FEATURE_TRAY
        expected.Add("TrayModule");
#endif
#if FEATURE_AUTOSTART
        expected.Add("AutostartModule");
#endif
#if FEATURE_GOAL_NOTIFICATIONS
        expected.Add("GoalNotificationsModule");
#endif
#if FEATURE_STATISTICS
        expected.Add("StatisticsModule");
#endif
#if FEATURE_UPDATE_CHECK
        expected.Add("UpdateCheckModule");
#endif
        Assert.Equal(expected.Order().ToList(), actual);
    }

    [Fact]
    public void Stopwatch_repository_is_the_capability_decorator()
    {
        var services = TestComposition.CreateServices();
        using var provider = services.BuildServiceProvider();
        try
        {
            Assert.IsType<CapabilityFilteredStopwatchRepository>(provider.GetRequiredService<IStopwatchRepository>());
            Assert.Same(FeatureModules.Capabilities, provider.GetRequiredService<StopwatchCapabilities>());
        }
        finally
        {
            // The composition test replaces the database with a temporary one; remove it again.
            var folder = Path.GetDirectoryName(provider.GetRequiredService<SqliteDatabase>().Path)!;
            Directory.Delete(folder, recursive: true);
        }
    }

    private static HashSet<Type> MappedViewModels(IServiceCollection services) =>
        Instances<ViewMapping>(services).Select(m => m.ViewModelType).ToHashSet();

    private static IEnumerable<T> Instances<T>(IServiceCollection services) where T : class =>
        services.Where(d => d.ServiceType == typeof(T) && d.ImplementationInstance is not null)
            .Select(d => (T)d.ImplementationInstance!);
}
