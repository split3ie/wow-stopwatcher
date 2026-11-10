using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Today;

/// <summary>One stopwatch row on the Today screen. Updated in place on every refresh.</summary>
public sealed partial class StopwatchRowViewModel : ObservableObject
{
    private readonly TodayViewModel _owner;
    private string? _color;

    public StopwatchRowViewModel(TodayViewModel owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    public string Id { get; }

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>The stopwatch color.</summary>
    [ObservableProperty]
    private IBrush? _brush;

    /// <summary>Progress fill behind the start/stop button: light tint, stronger once the goal is reached.</summary>
    [ObservableProperty]
    private IBrush? _fillBrush;

    [ObservableProperty]
    private BoxShadows _glow;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasGoal;

    [ObservableProperty]
    private bool _isIndependent;

    /// <summary>False when starting this stopwatch would exceed the concurrent limit (a running one stays stoppable).</summary>
    [ObservableProperty]
    private bool _canToggle = true;

    [ObservableProperty]
    private string? _toggleTip;

    [ObservableProperty]
    private bool _isGoalReached;

    [ObservableProperty]
    private GridLength _doneWidth = new(0, GridUnitType.Star);

    [ObservableProperty]
    private GridLength _leftWidth = new(1, GridUnitType.Star);

    [ObservableProperty]
    private string _currentText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _goalLine = string.Empty;

    [ObservableProperty]
    private string _goalSub = string.Empty;

    [ObservableProperty]
    private string? _shortcut;

    [ObservableProperty]
    private bool _hasShortcut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsLabel))]
    private bool _isOpen;

    [ObservableProperty]
    private string _totalHuman = string.Empty;

    [ObservableProperty]
    private string _toGoalLabel = string.Empty;

    [ObservableProperty]
    private string _toGoal = string.Empty;

    [ObservableProperty]
    private string _toggleLabel = string.Empty;

    /// <summary>True while this row is being dragged (shown lifted).</summary>
    [ObservableProperty]
    private bool _isDragging;

    public string DetailsLabel => IsOpen ? Loc.T("today.row.hideDetails") : Loc.T("today.row.showDetails");

    public string ReorderLabel => Loc.T("today.row.reorder", ("name", Name));

    public void Update(StopwatchEngineSnapshot snapshot, StopwatchDefinition definition)
    {
        var running = snapshot.IsRunning(definition.Id);
        var total = snapshot.GetTodayTotal(definition.Id);
        var hasGoal = definition.HasGoal;
        var status = snapshot.GetGoalStatus(definition.Id);
        var goal = status?.Goal ?? definition.Goal ?? TimeSpan.Zero;
        var reached = status?.IsReached ?? false;
        var fraction = status is null ? 0 : reached ? 1 : Math.Clamp(status.Progress / status.Goal, 0, 1);

        if (Name != definition.Name)
        {
            Name = definition.Name;
            OnPropertyChanged(nameof(ReorderLabel));
        }

        if (_color != definition.Color)
        {
            _color = definition.Color;
            var color = Color.Parse(definition.Color);
            Brush = new SolidColorBrush(color);
            Glow = new BoxShadows(
                new BoxShadow { Spread = 1, Color = color },
                [new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(0x22, color.R, color.G, color.B) }]);
        }

        var fillAlpha = (byte)(reached ? 0x2B : 0x17);
        var tint = Color.Parse(definition.Color);
        if (FillBrush is not ISolidColorBrush fill || fill.Color.A != fillAlpha || fill.Color.R != tint.R || fill.Color.G != tint.G || fill.Color.B != tint.B)
        {
            FillBrush = new SolidColorBrush(Color.FromArgb(fillAlpha, tint.R, tint.G, tint.B));
        }

        IsRunning = running;
        ToggleLabel = Loc.T(running ? "today.row.stopName" : "today.row.startName", ("name", definition.Name));
        HasGoal = hasGoal;
        IsIndependent = definition.IsIndependent;
        CanToggle = running || snapshot.CanStart(definition.Id);
        ToggleTip = CanToggle ? null : Loc.T("today.row.maxRunning", ("count", StopwatchEngine.MaxConcurrent));
        IsGoalReached = reached;
        DoneWidth = new GridLength(fraction, GridUnitType.Star);
        LeftWidth = new GridLength(1 - fraction, GridUnitType.Star);
        CurrentText = TimeFormat.Clock(snapshot.GetCurrentValue(definition.Id), withMs: true);
        TotalText = TimeFormat.Clock(total);
        GoalLine = hasGoal ? Loc.T("today.row.goal", ("goal", TimeFormat.GoalLabel(goal))) : Loc.T("today.row.noGoal");
        GoalSub = status is null
            ? Loc.T("today.row.setGoal")
            : GoalText.Sub(status, snapshot.TimeZone, fraction);
        Shortcut = definition.Shortcut is null ? null : SettingsShortcutKeys.ToDisplay(definition.Shortcut);
        HasShortcut = !string.IsNullOrWhiteSpace(definition.Shortcut);
        TotalHuman = TimeFormat.Human(total);
        ToGoalLabel = !hasGoal ? Loc.T("today.row.goalHeading") : reached ? Loc.T("today.row.overGoal") : Loc.T("today.row.toGoal");
        ToGoal = status is null ? Loc.T("today.row.none") : reached ? "+" + TimeFormat.Human(status.OverGoal) : TimeFormat.Human(status.Remaining);

        UpdateSessions(snapshot, definition.Id);
    }

    private void UpdateSessions(StopwatchEngineSnapshot snapshot, string id)
    {
        var sessions = snapshot.GetTodaySessions(id);
        var count = Math.Max(1, sessions.Count);
        CollectionSync.Resize(Sessions, count, () => new SessionItemViewModel());

        if (sessions.Count == 0)
        {
            Sessions[0].Number = "-";
            Sessions[0].Duration = Loc.T("today.row.noSessions");
            Sessions[0].Tag = string.Empty;
            return;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            Sessions[i].Number = $"{i + 1}.";
            Sessions[i].Duration = TimeFormat.Human(snapshot.GetDuration(sessions[i]));
            Sessions[i].Tag = sessions[i].IsOpen ? Loc.T("today.row.sessionRunning") : string.Empty;
        }
    }

    [RelayCommand]
    private void Toggle() => _owner.Toggle(Id);

    [RelayCommand]
    private void Reset() => _owner.RequestReset(this);

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    [RelayCommand]
    private void MoveUp() => _owner.Move(Id, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(Id, 1);
}
