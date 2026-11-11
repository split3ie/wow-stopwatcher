using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>An allocated (noted) entry in the invoice time dialog, with Edit and Delete.</summary>
public sealed partial class InvoiceTimeRowViewModel : ObservableObject
{
    private readonly InvoiceTimeViewModel _owner;

    public InvoiceTimeRowViewModel(InvoiceTimeViewModel owner, int index, StopwatchDayAllocation.Entry entry, bool isEditing)
    {
        _owner = owner;
        Index = index;
        TimeText = InvoiceTimeText.Format(entry.Minutes);
        Note = entry.Note;
        IsEditing = isEditing;
        IsEditable = owner.IsEditable;
        EditLabel = Loc.T("invoiceTime.edit.label", ("time", TimeText), ("note", entry.Note));
        DeleteLabel = Loc.T("invoiceTime.delete.label", ("time", TimeText), ("note", entry.Note));
    }

    /// <summary>Position of the entry in the allocation.</summary>
    public int Index { get; }

    public string TimeText { get; }

    public string Note { get; }

    /// <summary>True while the entry is being edited in the form (highlights the row).</summary>
    public bool IsEditing { get; }

    /// <summary>False when the invoice is sent, so Edit and Delete are hidden.</summary>
    public bool IsEditable { get; }

    public string EditLabel { get; }

    public string DeleteLabel { get; }

    [RelayCommand]
    private void Edit() => _owner.StartEdit(Index);

    [RelayCommand]
    private void Delete() => _owner.Delete(Index);
}
