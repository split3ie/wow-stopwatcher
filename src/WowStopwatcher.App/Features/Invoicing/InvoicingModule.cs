using Microsoft.Extensions.DependencyInjection;
using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Invoicing: PDF invoices made from DOCX templates. This module holds the services and the Invoices page.</summary>
public sealed class InvoicingModule : IFeatureModule
{
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IScriptEvaluator, JintScriptEvaluator>();
        services.AddSingleton<DocxTemplateScanner>();
        services.AddSingleton<DocxTemplateFiller>();
        services.AddSingleton<IPdfConverter, WordPdfConverter>();
        services.AddSingleton<IPdfConverter, LibreOfficePdfConverter>();
        services.AddSingleton<PdfConverterSelector>();
        services.AddSingleton<PdfPreviewRenderer>();
        services.AddSingleton<InvoiceTemplateStore>();
        services.AddSingleton<InvoicePdfBuilder>();
        services.AddSingleton<IInvoicingSettings, InvoicingSettings>();
        services.AddSingleton<InvoicingService>();
        services.AddSingleton<IExchangeRateService, ExchangeRateService>();
        services.AddSingleton<IInvoiceFileLauncher, SystemInvoiceFileLauncher>();
        services.AddSingleton<InvoicingNavigationRequest>();
        services.AddSingleton<IInvoiceDetailsViewModelFactory, InvoiceDetailsViewModelFactory>();
        services.AddSingleton(new ViewMapping(typeof(InvoiceDetailsViewModel), () => new InvoiceDetailsView()));
        services.AddTransient<InvoicingViewModel>();
        services.AddSingleton(new PageDescriptor(PageKeys.Invoicing, "invoicing.title", null, -10, typeof(InvoicingViewModel)));
        services.AddSingleton(new ViewMapping(typeof(InvoicingViewModel), () => new InvoicingView()));
        services.AddSingleton<IStatisticsStopwatchAction, InvoiceTimeStatisticsAction>();
        services.AddSingleton<IAppService>(sp => new TempFolderCleanupService(Path.GetTempPath(), sp.GetRequiredService<TimeProvider>()));
    }
}
