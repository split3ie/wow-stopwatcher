using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Features.InvoicesPin;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Tests.InvoicesPin;

public class InvoicesPinCompositionTests
{
    [Fact]
    public void InvoicesPin_replaces_the_shell_null_objects()
    {
        var services = TestComposition.CreateServices();

        Assert.Equal(typeof(InvoicesStartupPinSetup), services.Last(d => d.ServiceType == typeof(IStartupPinSetup)).ImplementationType);
        Assert.Equal(typeof(InvoicesSensitiveActionGuard), services.Last(d => d.ServiceType == typeof(ISensitiveActionGuard)).ImplementationType);
        Assert.Equal(typeof(InvoicesPageLock), services.Last(d => d.ServiceType == typeof(IPageLock)).ImplementationType);
        Assert.Contains(services, d => d.ServiceType == typeof(IAppService) && d.ImplementationFactory is not null);
        Assert.Contains(services, d => d.ServiceType == typeof(IWindowVisibilityAware));
        Assert.Contains(services, d => d.ServiceType == typeof(ISettingsGeneralItem) && d.ImplementationType == typeof(InvoicesPinSettingsViewModel));
    }
}
