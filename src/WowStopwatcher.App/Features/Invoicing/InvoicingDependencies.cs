using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The services the Invoicing page view models share; <see cref="InvoicingViewModel"/> collects them once and hands them to its parts.</summary>
internal sealed record InvoicingDependencies(
    ICompanyRepository Companies,
    IStopwatchRepository Stopwatches,
    InvoicingService Service,
    InvoiceTemplateStore Templates,
    DocxTemplateScanner Scanner,
    PdfConverterSelector Converter,
    IFilePickerService Picker,
    IClipboardService Clipboard,
    IInvoiceFileLauncher Files,
    IDialogService Dialog,
    IUiDispatcher Dispatcher,
    TimeProvider Time);
