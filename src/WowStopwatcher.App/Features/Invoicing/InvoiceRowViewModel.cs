using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One month in the invoices list, with its actions menu.</summary>
public sealed partial class InvoiceRowViewModel : ObservableObject
{
    private readonly InvoicesViewModel _owner;

    internal InvoiceRowViewModel(InvoicesViewModel owner, InvoiceMonthRow data)
    {
        _owner = owner;
        Data = data;
    }

    internal InvoiceMonthRow Data { get; }

    public InvoicePeriod Period => Data.Period;

    public string MonthText => InvoicingFormat.Month(Data.Period);

    public bool IsCurrent => Data.IsCurrentMonth;

    public bool IsDraft => Data.Status == InvoiceMonthStatus.Draft;

    public bool IsPending => Data.Status == InvoiceMonthStatus.Pending;

    public bool IsSent => Data.Status == InvoiceMonthStatus.Sent;

    public bool HasFileName => Data.Invoice?.FilePath is not null;

    /// <summary>The generated file name, with a note when the file is gone; "Not generated yet" otherwise.</summary>
    public string FileText => Data.Invoice?.FilePath is { } path
        ? Data.FileExists ? Path.GetFileName(path) : Loc.T("invoicing.list.fileMissing", ("name", Path.GetFileName(path)))
        : Loc.T("invoicing.list.notGenerated");

    public string HoursText => InvoicingFormat.Duration(Data.InvoicedMinutes);

    public bool HasReported => Data.ReportedMinutes is not null;

    /// <summary>The reported time against the tracked time, empty without reported hours.</summary>
    public string ReportedDifferenceText => Data.ReportedMinutes is { } reported
        ? InvoicingFormat.ReportedDifference(reported, Data.TrackedMinutes)
        : string.Empty;

    public bool HasUnallocated => Data.UnallocatedMinutes > 0;

    public string UnallocatedText => Loc.T("invoicing.list.unallocated", ("time", InvoicingFormat.Duration(Data.UnallocatedMinutes)));

    /// <summary>The amount, or the mask while amounts are hidden.</summary>
    public string AmountText => _owner.AmountsHidden ? MoneyMask.Mask : InvoicingFormat.Money(Data.Amount, Data.Currency);

    public string StatusText => Data.Status switch
    {
        InvoiceMonthStatus.Sent => Loc.T("invoicing.status.sent"),
        InvoiceMonthStatus.Pending => Loc.T("invoicing.status.pending"),
        _ => Loc.T("invoicing.status.draft"),
    };

    public string GenerateText => HasFileName ? Loc.T("invoicing.action.generateAgain") : Loc.T("invoicing.action.generate");

    public string MarkText => IsSent ? Loc.T("invoicing.action.markPending") : Loc.T("invoicing.action.markSent");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportedInvalid), nameof(CanSaveReported), nameof(ReportedEditorHint))]
    private string _reportedInput = string.Empty;

    [ObservableProperty]
    private bool _isEditingReported;

    public string TrackedText => InvoicingFormat.Duration(Data.TrackedMinutes);

    public string ReportedEditorLabel => Loc.T("invoicing.list.reported.tracked", ("time", TrackedText));

    public bool ReportedInvalid => ReportedInput.Trim().Length > 0 && !TryParseReported(out _);

    public bool CanSaveReported => ReportedInput.Trim().Length > 0 && !ReportedInvalid;

    public string ReportedEditorHint => ReportedInvalid ? Loc.T("invoicing.details.reported.invalid") : Loc.T("invoicing.details.reported.hint");

    private bool TryParseReported(out int minutes) =>
        InvoiceTimeText.TryParse(ReportedInput, out minutes) && InvoiceBilling.IsValidReportedMinutes(minutes);

    internal void BeginReportedEdit()
    {
        ReportedInput = InvoiceTimeText.FormatPadded(Data.ReportedMinutes ?? Data.InvoicedMinutes);
        IsEditingReported = true;
    }

    internal void EndReportedEdit() => IsEditingReported = false;

    [RelayCommand]
    private void SaveReported()
    {
        if (CanSaveReported && TryParseReported(out var minutes))
        {
            _owner.SaveReported(this, minutes);
        }
    }

    [RelayCommand]
    private void ClearReported() => _owner.SaveReported(this, null);

    [RelayCommand]
    private void CancelReported() => EndReportedEdit();

    internal void RefreshMask() => OnPropertyChanged(nameof(AmountText));

    private bool CanOpenFile() => Data.FileExists;

    private bool CanGenerate() => !IsSent;

    private bool CanSetReported() => !IsSent;

    private bool CanMark() => !IsDraft;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void OpenFile() => _owner.OpenFile(this);

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private void ShowInFolder() => _owner.ShowInFolder(this);

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private Task GenerateAsync() => _owner.GenerateAsync(this);

    [RelayCommand(CanExecute = nameof(CanMark))]
    private void ToggleSent() => _owner.ToggleSent(this);

    [RelayCommand(CanExecute = nameof(CanSetReported))]
    private void SetReported() => _owner.OpenReportedEditor(this);

    [RelayCommand]
    private void ShowDetails() => _owner.ShowDetails(this);
}
