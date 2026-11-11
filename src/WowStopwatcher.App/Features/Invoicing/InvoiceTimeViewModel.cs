using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>
/// The time and notes of one stopwatch on one day of a company's invoice. Only allocated time (a line with a note) is billed;
/// every change goes through <see cref="StopwatchDayAllocation"/> and <see cref="InvoicingService.EditDay"/>.
/// </summary>
public sealed partial class InvoiceTimeViewModel : ObservableObject
{
    private const int NoEntry = -1;

    private readonly InvoicingService _service;
    private readonly string _companyId;
    private readonly InvoicePeriod _period;
    private readonly DateOnly _date;
    private readonly StopwatchDefinition _stopwatch;
    private readonly Action _close;
    private readonly Action? _openInvoice;
    private StopwatchDayAllocation _allocation;
    private int _editIndex = NoEntry;

    public InvoiceTimeViewModel(
        InvoicingService service,
        string companyId,
        string companyName,
        StopwatchDefinition stopwatch,
        DateOnly date,
        Action close,
        Action? openInvoice = null)
    {
        _service = service;
        _companyId = companyId;
        _stopwatch = stopwatch;
        _date = date;
        _close = close;
        _openInvoice = openInvoice;
        _period = InvoicePeriod.From(date);
        _allocation = StopwatchDayAllocation.From([], stopwatch.Id, stopwatch.Name, 0);

        Title = Loc.T("invoiceTime.title", ("name", stopwatch.Name), ("day", date.ToString(Loc.T("stats.format.dayWithYear"), Loc.Culture)));
        Subtitle = Loc.T("invoiceTime.subtitle", ("company", companyName), ("month", _period.FirstDay.ToString("MMMM yyyy", Loc.Culture)));
        Brush = Avalonia.Media.Brush.Parse(stopwatch.Color);
        var range = service.GetDayRanges(companyId, _period).FirstOrDefault(r => r.Date == date);
        if (range is { IsCalendarDay: false })
        {
            const string dayFormat = "d MMM";
            DayRangeText = Loc.T(
                "invoiceTime.dayRange",
                ("from", date.ToString(dayFormat, Loc.Culture) + " " + DayStartText.Format(range.StartMinutes)),
                ("to", date.AddDays(1).ToString(dayFormat, Loc.Culture) + " " + DayStartText.Format(range.EndMinutes)),
                ("time", DayStartText.Format(range.StartMinutes)));
        }

        Reload();
    }

    /// <summary>Explains an invoice day that is not a calendar day; empty otherwise.</summary>
    public string DayRangeText { get; } = string.Empty;

    public bool HasDayRange => DayRangeText.Length > 0;

    public string Title { get; }

    /// <summary>Company and month; the status follows in <see cref="StatusText"/>.</summary>
    public string Subtitle { get; }

    public IBrush Brush { get; }

    public ObservableCollection<InvoiceTimeRowViewModel> Rows { get; } = [];

    public ObservableCollection<InvoiceTimeMergedRow> MergedRows { get; } = [];

    public bool HasMergedRows => MergedRows.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable), nameof(StatusText), nameof(CanAllocate))]
    private bool _isSent;

    public bool IsEditable => !IsSent;

    public string StatusText => Loc.T(IsSent ? "invoiceTime.status.sent" : "invoiceTime.status.pending");

    [ObservableProperty]
    private string _trackedText = string.Empty;

    [ObservableProperty]
    private string _billedText = string.Empty;

    [ObservableProperty]
    private string _overText = string.Empty;

    [ObservableProperty]
    private bool _isOver;

    [ObservableProperty]
    private string _unallocatedText = string.Empty;

    /// <summary>True when some tracked time is left unallocated (the Allocate button is then the main action).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoUnallocated))]
    private bool _hasUnallocated;

    public bool NoUnallocated => !HasUnallocated;

    /// <summary>True when the Allocate button applies: the invoice is not sent and no form is open.</summary>
    public bool CanAllocate => !IsSent && !IsFormOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAllocate), nameof(FormTitle))]
    private bool _isFormOpen;

    public string FormTitle => Loc.T(_editIndex == NoEntry ? "invoiceTime.form.allocate" : "invoiceTime.form.edit");

    [ObservableProperty]
    private string _timeInput = string.Empty;

    [ObservableProperty]
    private string _noteInput = string.Empty;

    [ObservableProperty]
    private string _hint = string.Empty;

    /// <summary>True when the typed time is more than the tracked time that can still be allocated.</summary>
    [ObservableProperty]
    private bool _isHintOver;

    [ObservableProperty]
    private bool _canSave;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    partial void OnTimeInputChanged(string value) => RefreshForm();

    partial void OnNoteInputChanged(string value) => RefreshForm();

    [RelayCommand]
    private void StartAllocate()
    {
        if (IsSent)
        {
            return;
        }

        OpenForm(NoEntry, InvoiceTimeText.FormatShort(Math.Max(_allocation.Unallocated, 0)), string.Empty);
    }

    internal void StartEdit(int index)
    {
        if (IsSent)
        {
            return;
        }

        var entry = _allocation.Entries[index];
        OpenForm(index, InvoiceTimeText.FormatShort(entry.Minutes), entry.Note);
    }

    [RelayCommand]
    private void Cancel()
    {
        IsFormOpen = false;
        _editIndex = NoEntry;
        OnPropertyChanged(nameof(FormTitle));
        RebuildRows();
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave || !InvoiceTimeText.TryParse(TimeInput, out var minutes))
        {
            return;
        }

        var next = _editIndex == NoEntry
            ? _allocation.Allocate(minutes, NoteInput)
            : _allocation.Edit(_editIndex, minutes, NoteInput);
        Apply(next);
    }

    internal void Delete(int index)
    {
        if (!IsSent)
        {
            Apply(_allocation.Delete(index));
        }
    }

    [RelayCommand]
    private void Close() => _close();

    /// <summary>True when the dialog can open the whole invoice (the Invoicing page is part of the app).</summary>
    public bool CanOpenInvoice => _openInvoice is not null;

    [RelayCommand]
    private void OpenInvoice() => _openInvoice?.Invoke();

    private void Apply(StopwatchDayAllocation next)
    {
        var result = _service.EditDay(_companyId, _period, _date, lines => next.ApplyTo(lines, _date));
        switch (result)
        {
            case EditDayResult.Saved:
                ErrorText = string.Empty;
                IsFormOpen = false;
                _editIndex = NoEntry;
                OnPropertyChanged(nameof(FormTitle));
                Reload();
                break;
            case EditDayResult.InvoiceSent:
                IsFormOpen = false;
                Reload();
                ErrorText = Loc.T("invoiceTime.error.sent");
                break;
            default:
                ErrorText = Loc.T("invoiceTime.error.failed");
                break;
        }
    }

    private void OpenForm(int index, string time, string note)
    {
        _editIndex = index;
        ErrorText = string.Empty;
        IsFormOpen = true;
        OnPropertyChanged(nameof(FormTitle));
        TimeInput = time;
        NoteInput = note;
        RefreshForm();
        RebuildRows();
    }

    private void Reload()
    {
        var day = _service.GetStopwatchDay(_companyId, _period, _date, _stopwatch.Id);
        IsSent = day.IsSent;
        _allocation = StopwatchDayAllocation.From(day.Lines, _stopwatch.Id, _stopwatch.Name, day.TrackedMinutes);

        TrackedText = InvoiceTimeText.Format(_allocation.TrackedMinutes);
        BilledText = InvoiceTimeText.Format(_allocation.BilledMinutes);
        IsOver = _allocation.OverMinutes > 0;
        OverText = Loc.T("invoiceTime.over", ("time", InvoiceTimeText.Format(_allocation.OverMinutes)));
        UnallocatedText = InvoiceTimeText.Format(_allocation.Unallocated);
        HasUnallocated = _allocation.Unallocated > 0;

        MergedRows.Clear();
        foreach (var line in _allocation.MergedLines)
        {
            MergedRows.Add(new InvoiceTimeMergedRow(
                InvoiceTimeText.Format(line.Minutes),
                line.Note,
                string.Join(" + ", line.Sources.Select(s => s.StopwatchName))));
        }

        OnPropertyChanged(nameof(HasMergedRows));
        RebuildRows();
        RefreshForm();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        for (var i = 0; i < _allocation.Entries.Count; i++)
        {
            Rows.Add(new InvoiceTimeRowViewModel(this, i, _allocation.Entries[i], IsFormOpen && i == _editIndex));
        }
    }

    private void RefreshForm()
    {
        if (!IsFormOpen)
        {
            CanSave = false;
            return;
        }

        // The editing entry's own time can be reused; beyond that only the unallocated time is free.
        var free = _allocation.Unallocated + (_editIndex == NoEntry ? 0 : _allocation.Entries[_editIndex].Minutes);
        var parsed = InvoiceTimeText.TryParse(TimeInput, out var minutes);
        var beyond = parsed && minutes > free;
        IsHintOver = beyond;
        Hint = beyond
            ? Loc.T("invoiceTime.hint.over", ("time", InvoiceTimeText.Format(minutes - free)))
            : Loc.T("invoiceTime.hint.free", ("time", InvoiceTimeText.Format(free)));
        CanSave = parsed && minutes > 0 && NoteInput.Trim().Length > 0;
    }
}
