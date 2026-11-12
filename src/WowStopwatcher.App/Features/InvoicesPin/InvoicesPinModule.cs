using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>The Invoices PIN: locks the Invoices page, guards sensitive actions and adds a PIN step at startup. Replaces the shell's null objects.</summary>
public sealed class InvoicesPinModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<InvoicesPinService>();
        services.AddSingleton<IRecoveryCodeVerifier, DatabaseRecoveryCodeVerifier>();
        services.AddSingleton<InvoicesPinPrompts>();
        services.AddSingleton<IStartupPinSetup, InvoicesStartupPinSetup>();
        services.ConfigureShell(o => o.StartupPinStep = true);
        services.AddSingleton<ISensitiveActionGuard, InvoicesSensitiveActionGuard>();
        services.AddSingleton<InvoicesAutoLock>();
        services.AddSingleton<IAppService>(sp => sp.GetRequiredService<InvoicesAutoLock>());
        services.AddSingleton<IWindowVisibilityAware>(sp => sp.GetRequiredService<InvoicesAutoLock>());
        services.AddSingleton<InvoicesLockViewModel>();
        services.AddSingleton(new ViewMapping(typeof(InvoicesLockViewModel), () => new InvoicesLockView()));
        services.AddSingleton<IPageLock, InvoicesPageLock>();
        services.AddTransient<ISettingsGeneralItem, InvoicesPinSettingsViewModel>();
        services.AddSingleton(new ViewMapping(typeof(InvoicesPinSettingsViewModel), () => new InvoicesPinSettingsView()));
    }
}
