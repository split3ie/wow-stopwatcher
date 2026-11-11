using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>A stopwatch chip: toggles whether the stopwatch is billed to the company. One owned by another company is disabled.</summary>
public sealed partial class StopwatchChipViewModel : ObservableObject
{
    private readonly Action<StopwatchChipViewModel> _changed;

    internal StopwatchChipViewModel(StopwatchDefinition stopwatch, bool isAssigned, string? otherOwnerName, Action<StopwatchChipViewModel> changed)
    {
        _changed = changed;
        StopwatchId = stopwatch.Id;
        Name = stopwatch.IsDeleted ? Loc.T("invoicing.chip.hidden", ("name", stopwatch.Name)) : stopwatch.Name;
        Brush = new SolidColorBrush(InvoicingColors.Parse(stopwatch.Color));
        OwnerName = otherOwnerName;
        _isAssigned = isAssigned;
    }

    public string StopwatchId { get; }

    public string Name { get; }

    public IBrush Brush { get; }

    /// <summary>The name of the other company that bills this stopwatch; null when it is free or billed to this company.</summary>
    public string? OwnerName { get; }

    public bool IsTaken => OwnerName is not null;

    public bool IsFree => OwnerName is null;

    public string OwnerText => OwnerName is null ? string.Empty : Loc.T("invoicing.chip.owner", ("company", OwnerName));

    [ObservableProperty]
    private bool _isAssigned;

    partial void OnIsAssignedChanged(bool value) => _changed(this);
}
