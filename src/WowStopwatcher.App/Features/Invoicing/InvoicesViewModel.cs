using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>The invoices list of one company: a row per month and the actions to generate, open, mark and show details.</summary>
public sealed partial class InvoicesViewModel : ObservableObject
{
    private readonly InvoicingDependencies _d;
    private readonly string _companyId;
    private readonly Action<InvoicePeriod> _showDetails;

    internal InvoicesViewModel(InvoicingDependencies dependencies, string companyId, bool amountsHidden, Action<InvoicePeriod> showDetails)
    {
        _d = dependencies;
        _companyId = companyId;
        _amountsHidden = amountsHidden;
        _showDetails = showDetails;
        Refresh();
    }

    public ObservableCollection<InvoiceRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    [ObservableProperty]
    private bool _amountsHidden;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isStatusError;

    public bool HasStatus => StatusText.Length > 0;

    partial void OnAmountsHiddenChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.RefreshMask();
        }
    }

    internal void SetAmountsHidden(bool hidden) => AmountsHidden = hidden;

    /// <summary>Reads the months again.</summary>
    public void Refresh()
    {
        Rows.Clear();
        try
        {
            foreach (var month in _d.Service.GetMonths(_companyId))
            {
                Rows.Add(new InvoiceRowViewModel(this, month));
            }
        }
        catch (InvalidOperationException)
        {
            // The company no longer exists; the page reloads its tabs.
        }

        OnPropertyChanged(nameof(HasRows));
    }

    private void Report(string text, bool isError)
    {
        StatusText = text;
        IsStatusError = isError;
    }

    internal void OpenFile(InvoiceRowViewModel row)
    {
        if (row.Data.Invoice?.FilePath is not { } path || !_d.Files.OpenFile(path))
        {
            Report(Loc.T("invoicing.list.openFailed"), true);
        }
    }

    internal void ShowInFolder(InvoiceRowViewModel row)
    {
        if (row.Data.Invoice?.FilePath is not { } path || !_d.Files.ShowInFolder(path))
        {
            Report(Loc.T("invoicing.list.openFailed"), true);
        }
    }

    internal void ShowDetails(InvoiceRowViewModel row) => _showDetails(row.Period);

    /// <summary>Warns about unallocated time, picks the destination and makes the PDF.</summary>
    internal async Task GenerateAsync(InvoiceRowViewModel row)
    {
        if (IsBusy)
        {
            return;
        }

        if (row.HasUnallocated)
        {
            _d.Dialog.Show(
                Loc.T("invoicing.generate.unallocatedTitle"),
                Loc.T("invoicing.generate.unallocatedBody", ("time", InvoicingFormat.Duration(row.Data.UnallocatedMinutes))),
                Loc.T("invoicing.generate.anyway"),
                () => _ = ChooseDestinationAsync(row));
            return;
        }

        await ChooseDestinationAsync(row);
    }

    private async Task ChooseDestinationAsync(InvoiceRowViewModel row)
    {
        if (_d.Companies.Get(_companyId) is not { } company)
        {
            return;
        }

        string suggested;
        try
        {
            suggested = _d.Service.SuggestFileName(_companyId, row.Period);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (company.AskDestination)
        {
            // The system dialog asks before replacing an existing file.
            var picked = await _d.Picker.PickSaveFileAsync(Loc.T("invoicing.generate.saveTitle"), suggested, "pdf");
            if (picked is not null)
            {
                await RunGenerateAsync(row, picked);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(company.OutputFolder))
        {
            Report(Loc.T("invoicing.generate.noFolder"), true);
            return;
        }

        var path = Path.Combine(company.OutputFolder, suggested);
        if (File.Exists(path))
        {
            _d.Dialog.Show(
                Loc.T("invoicing.generate.replaceTitle"),
                Loc.T("invoicing.generate.replaceBody", ("name", suggested)),
                Loc.T("invoicing.generate.replace"),
                () => _ = RunGenerateAsync(row, path));
            return;
        }

        await RunGenerateAsync(row, path);
    }

    private async Task RunGenerateAsync(InvoiceRowViewModel row, string path)
    {
        IsBusy = true;
        Report(string.Empty, false);
        try
        {
            var result = await Task.Run(() => _d.Service.GenerateAsync(_companyId, row.Period, path, CancellationToken.None));
            switch (result.Outcome)
            {
                case GenerateOutcome.Generated:
                    Report(Loc.T("invoicing.generate.done", ("name", Path.GetFileName(result.FilePath ?? path))), false);
                    break;
                case GenerateOutcome.Blocked:
                    Report(InvoicingMessages.Blocks(result.Blocks ?? []), true);
                    break;
                case GenerateOutcome.NothingAllocated:
                    Report(Loc.T("invoicing.generate.nothing"), true);
                    break;
                case GenerateOutcome.FileInUse:
                    Report(Loc.T("invoicing.generate.inUse"), true);
                    break;
                default:
                    Report(InvoicingMessages.Pdf(result.PdfError, result.Detail), true);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            Report(ex.Message, true);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>Marks the invoice as sent (asking first when it changed since the file was made) or as pending again.</summary>
    internal void ToggleSent(InvoiceRowViewModel row)
    {
        if (row.IsSent)
        {
            if (_d.Service.MarkPending(_companyId, row.Period))
            {
                Report(Loc.T("invoicing.mark.pendingDone"), false);
            }

            Refresh();
            return;
        }

        MarkSent(row, confirmChanged: false);
    }

    private void MarkSent(InvoiceRowViewModel row, bool confirmChanged)
    {
        switch (_d.Service.MarkSent(_companyId, row.Period, confirmChanged))
        {
            case MarkSentResult.ChangedSinceGeneration:
                _d.Dialog.Show(
                    Loc.T("invoicing.mark.changedTitle"),
                    Loc.T("invoicing.mark.changedBody"),
                    Loc.T("invoicing.mark.changedConfirm"),
                    () => MarkSent(row, confirmChanged: true));
                return;
            case MarkSentResult.Sent:
                Report(Loc.T("invoicing.mark.sentDone"), false);
                break;
        }

        Refresh();
    }
}
