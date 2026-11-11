using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One day of the invoice details: its header (sum, badges, drift, reset) and lines.</summary>
public sealed partial class InvoiceDetailsDayViewModel : ObservableObject
{
    private readonly InvoiceDetailsViewModel _owner;

    internal InvoiceDetailsDayViewModel(InvoiceDetailsViewModel owner, DateOnly date, string label)
    {
        _owner = owner;
        Date = date;
        Label = label;
    }

    public DateOnly Date { get; }

    public string Label { get; }

    public ObservableCollection<InvoiceDetailsLineViewModel> Lines { get; } = [];

    /// <summary>Minutes tracked that day; shown in the adjust form.</summary>
    public int TrackedMinutes { get; private set; }

    [ObservableProperty]
    private string _sumText = string.Empty;

    [ObservableProperty]
    private bool _isEdited;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _runningText = string.Empty;

    [ObservableProperty]
    private bool _hasDrift;

    [ObservableProperty]
    private string _driftText = string.Empty;

    [ObservableProperty]
    private bool _canApplyDrift;

    [ObservableProperty]
    private bool _canReset;

    /// <summary>"20:00 → 20:00"; empty for a plain calendar day.</summary>
    [ObservableProperty]
    private string _rangeText = string.Empty;

    [ObservableProperty]
    private bool _hasRange;

    /// <summary>Takes the values of the day.</summary>
    internal void Apply(InvoiceDay day, InvoiceDayRange? range, IReadOnlyList<string> running, bool isEditable)
    {
        HasRange = range is { IsCalendarDay: false };
        RangeText = range is null || range.IsCalendarDay
            ? string.Empty
            : Loc.T(
                "invoicing.details.dayRange",
                ("from", DayStartText.Format(range.StartMinutes)),
                ("to", DayStartText.Format(range.EndMinutes)));
        TrackedMinutes = day.TrackedMinutes;
        SumText = InvoicingFormat.Duration(day.InvoicedMinutes);
        IsEdited = day.Edited;
        IsRunning = day.HasRunningSession;
        RunningText = Loc.T("invoicing.details.running", ("name", string.Join(", ", running)));
        HasDrift = day.DriftMinutes != 0;
        DriftText = Loc.T(
            "invoicing.details.drift",
            ("delta", (day.DriftMinutes > 0 ? "+" : "−") + InvoicingFormat.Duration(Math.Abs(day.DriftMinutes))));
        CanApplyDrift = isEditable;
        CanReset = isEditable && day.Edited;
    }

    [RelayCommand]
    private void ApplyDrift() => _owner.ApplyDrift(this);

    [RelayCommand]
    private void Reset() => _owner.ResetDay(this);
}
