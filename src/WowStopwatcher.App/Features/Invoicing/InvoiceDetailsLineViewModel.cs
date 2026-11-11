using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One line of an invoice day: its stopwatch, time and an editable note. Only lines with a note are billed.</summary>
public sealed partial class InvoiceDetailsLineViewModel : ObservableObject
{
    private readonly InvoiceDetailsViewModel _owner;
    private readonly string _dayLabel;

    internal InvoiceDetailsLineViewModel(
        InvoiceDetailsViewModel owner,
        InvoiceLine line,
        int index,
        string dayLabel,
        IBrush dotBrush,
        bool isEditable)
    {
        _owner = owner;
        _dayLabel = dayLabel;
        Date = line.Date;
        Index = index;
        DotBrush = dotBrush;
        IsEditable = isEditable;
        NameText = line.IsMerged ? Loc.T("invoicing.details.merged") : line.Sources[0].StopwatchName;
        PartsText = string.Join(" + ", line.Sources.Select(s => s.StopwatchName));
        IsMerged = line.IsMerged;
        Minutes = line.Minutes;
        _timeText = InvoiceTimeText.Format(line.Minutes);
        _note = line.Note;
    }

    /// <summary>The day of the line.</summary>
    public DateOnly Date { get; }

    /// <summary>Position of the line within its day.</summary>
    public int Index { get; }

    public IBrush DotBrush { get; }

    public bool IsEditable { get; }

    public bool IsReadOnly => !IsEditable;

    public string NameText { get; }

    /// <summary>The stopwatches of a merged line, joined with " + ".</summary>
    public string PartsText { get; }

    public bool IsMerged { get; }

    public int Minutes { get; private set; }

    public string SelectLabel => Loc.T("invoicing.details.selectLine", ("name", PartsText), ("time", TimeText), ("day", _dayLabel));

    public string NoteLabel => Loc.T("invoicing.details.noteLabel", ("name", PartsText), ("day", _dayLabel));

    [ObservableProperty]
    private string _timeText;

    /// <summary>The note as typed; saved a moment after the last change.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBilled), nameof(IsUnallocated))]
    private string _note;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True when the line has a note, so it is in the invoice.</summary>
    public bool IsBilled => !string.IsNullOrWhiteSpace(Note);

    public bool IsUnallocated => !IsBilled;

    partial void OnNoteChanged(string value) => _owner.NoteEdited(this);

    partial void OnIsSelectedChanged(bool value) => _owner.SelectionChanged();

    /// <summary>True when the stored line has the same stopwatches, so the row can be updated in place.</summary>
    internal bool Matches(InvoiceLine line) => PartsText == string.Join(" + ", line.Sources.Select(s => s.StopwatchName));

    internal void UpdateTime(int minutes)
    {
        Minutes = minutes;
        TimeText = InvoiceTimeText.Format(minutes);
        OnPropertyChanged(nameof(SelectLabel));
    }
}
