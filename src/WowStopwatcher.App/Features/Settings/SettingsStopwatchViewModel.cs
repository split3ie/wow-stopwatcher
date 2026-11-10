using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>One editable stopwatch card on the Settings page. Every change is saved immediately.</summary>
public sealed partial class SettingsStopwatchViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;
    private readonly IShortcutService _hotkeys;
    private readonly StopwatchCapabilities _capabilities;
    private string _savedName;

    public SettingsStopwatchViewModel(SettingsViewModel owner, StopwatchDefinition definition, IShortcutService hotkeys, StopwatchCapabilities capabilities)
    {
        _owner = owner;
        _hotkeys = hotkeys;
        _capabilities = capabilities;
        Id = definition.Id;
        _name = definition.Name;
        _savedName = definition.Name;
        _color = definition.Color;
        _brush = ParseBrush(definition.Color);
        _hasGoal = definition.HasGoal;
        _isIndependent = definition.IsIndependent;
        _isPermanent = definition.IsPermanent;
        _goalPerCycle = definition.GoalPerCycle;
        _goalText = definition.HasGoal ? TimeFormat.FormatGoal(definition.Goal!.Value) : SettingsGoalMask.Empty;
        _shortcut = definition.Shortcut ?? string.Empty;
        Swatches =
        [
            .. SettingsSwatchViewModel.Palette.Select(p => new SettingsSwatchViewModel(
                this, p.Name, p.Hex, string.Equals(p.Hex, definition.Color, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    public string Id { get; }

    /// <summary>False when the build has no Hotkeys feature; the card then hides its shortcut field. The stored shortcut is kept.</summary>
    public bool AreShortcutsAvailable => _hotkeys.IsAvailable;

    /// <summary>False when the build has no stopwatch goals; the card then hides the goal field.</summary>
    public bool AreGoalsAvailable => _capabilities.Goals;

    /// <summary>False when the build has no independent stopwatches; the card then hides the toggle.</summary>
    public bool IsIndependentAvailable => _capabilities.Independent;

    /// <summary>False when the build has no permanent stopwatches; the card then hides the toggle.</summary>
    public bool IsPermanentAvailable => _capabilities.Permanent;

    /// <summary>False when the build has neither independent nor permanent stopwatches; the card then hides the Behavior section.</summary>
    public bool AreBehaviorOptionsAvailable => _capabilities.Independent || _capabilities.Permanent;

    public ObservableCollection<SettingsSwatchViewModel> Swatches { get; }

    /// <summary>Set on a newly added card so the view focuses and selects its name once.</summary>
    public bool FocusNameRequested { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(SectionLabel), nameof(GripLabel), nameof(DeleteLabel), nameof(GoalLabel))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorName), nameof(ColorLabel))]
    private string _color;

    [ObservableProperty]
    private IBrush _brush;

    [ObservableProperty]
    private bool _hasGoal;

    /// <summary>True when this stopwatch runs alongside the others instead of replacing the running one.</summary>
    [ObservableProperty]
    private bool _isIndependent;

    /// <summary>True when this stopwatch is kept every day instead of disappearing after a full day without use.</summary>
    [ObservableProperty]
    private bool _isPermanent;

    /// <summary>True when the goal keeps filling across days until reached, then a new goal starts; false resets it every day.</summary>
    [ObservableProperty]
    private bool _goalPerCycle;

    /// <summary>Goal as "hh:mm:ss". Kept when the goal checkbox is cleared.</summary>
    [ObservableProperty]
    private string _goalText;

    /// <summary>Stored shortcut text, empty when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText), nameof(ShortcutHint), nameof(HasShortcutHintWarning))]
    private string _shortcut;

    /// <summary>True when the system refused to register this stopwatch's shortcut (taken by another app).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutHint), nameof(HasShortcutHintWarning))]
    private bool _isShortcutTaken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText), nameof(ShortcutPlaceholder), nameof(ShortcutHint), nameof(HasShortcutHintWarning))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText))]
    private string _liveText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutHint), nameof(HasShortcutWarning), nameof(HasShortcutHintWarning))]
    private string _shortcutWarning = string.Empty;

    [ObservableProperty]
    private bool _isDragging;

    /// <summary>Palette name of the current color, or "Custom" when it is not a palette color.</summary>
    public string ColorName => SettingsSwatchViewModel.Palette
        .Where(p => string.Equals(p.Hex, Color, StringComparison.OrdinalIgnoreCase))
        .Select(p => SettingsSwatchViewModel.LocalizedName(p.Name))
        .FirstOrDefault(Loc.T("settings.color.custom"));

    public string ColorLabel => Loc.T("settings.stopwatch.colorLabel", ("name", ColorName));

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Loc.T("settings.stopwatch.untitled") : Name;

    public string SectionLabel => Loc.T("settings.stopwatch.sectionLabel", ("name", DisplayName));

    public string GripLabel => IsNamed ? Loc.T("settings.stopwatch.gripLabel", ("name", Name)) : Loc.T("settings.stopwatch.gripLabelUnnamed");

    public string DeleteLabel => IsNamed ? Loc.T("settings.stopwatch.deleteLabel", ("name", Name)) : Loc.T("settings.stopwatch.deleteLabelUnnamed");

    public string GoalLabel => IsNamed ? Loc.T("settings.stopwatch.goalLabel", ("name", Name)) : Loc.T("settings.stopwatch.goalLabelUnnamed");

    public string ShortcutText => SettingsShortcutKeys.ToDisplay(IsRecording ? LiveText : Shortcut);

    public string ShortcutPlaceholder => IsRecording ? Loc.T("settings.stopwatch.pressKeys") : Loc.T("settings.stopwatch.notSet");

    public bool HasShortcutWarning => ShortcutWarning.Length > 0;

    private bool ShowsTakenMessage => IsShortcutTaken && !IsRecording && Shortcut.Length > 0;

    /// <summary>True when the hint under the shortcut field is an error (invalid key or shortcut taken).</summary>
    public bool HasShortcutHintWarning => HasShortcutWarning || ShowsTakenMessage;

    public string ShortcutHint => ShortcutWarning.Length > 0
        ? ShortcutWarning
        : IsRecording ? Loc.T("settings.stopwatch.recordingHint")
        : ShowsTakenMessage ? Loc.T("settings.stopwatch.shortcutTaken") : string.Empty;

    /// <summary>The goal to store: null when unchecked or 00:00:00.</summary>
    public TimeSpan? Goal => HasGoal && TimeFormat.TryParseGoal(GoalText, out var goal) ? goal : null;

    /// <summary>The shortcut to store: null when none.</summary>
    public string? StoredShortcut => Shortcut.Length > 0 ? Shortcut : null;

    /// <summary>The name to store: the last non-empty name typed.</summary>
    internal string SavedName => _savedName;

    partial void OnNameChanged(string value)
    {
        // An empty name is not saved while typing; the last saved name is restored when the field loses focus.
        if (!string.IsNullOrWhiteSpace(value))
        {
            _savedName = value;
            _owner.Save(this);
        }
    }

    partial void OnHasGoalChanged(bool value) => _owner.Save(this);

    partial void OnIsIndependentChanged(bool value) => _owner.SaveType(this);

    partial void OnIsPermanentChanged(bool value) => _owner.SavePermanent(this);

    partial void OnGoalPerCycleChanged(bool value) => _owner.SavePerCycle(this);

    partial void OnGoalTextChanged(string value)
    {
        if (HasGoal)
        {
            _owner.Save(this);
        }
    }

    /// <summary>Called when the name field loses focus: puts the last saved name back if it was cleared.</summary>
    public void CommitName()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = _savedName;
        }
    }

    internal void SelectColor(string hex)
    {
        Color = hex;
        Brush = ParseBrush(hex);
        foreach (var swatch in Swatches)
        {
            swatch.IsSelected = string.Equals(swatch.Hex, hex, StringComparison.OrdinalIgnoreCase);
        }

        _owner.Save(this);
    }

    /// <summary>True while the flyout shows the custom color picker instead of the swatches.</summary>
    [ObservableProperty]
    private bool _isCustomColorOpen;

    /// <summary>The color being edited in the custom picker. Saved only by <see cref="ApplyCustomColor"/>.</summary>
    [ObservableProperty]
    private Avalonia.Media.Color _pendingColor;

    [RelayCommand]
    private void OpenCustomColor()
    {
        PendingColor = Avalonia.Media.Color.TryParse(Color, out var current) ? current : default;
        IsCustomColorOpen = true;
    }

    [RelayCommand]
    private void ApplyCustomColor()
    {
        var c = PendingColor;
        SelectColor($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        IsCustomColorOpen = false;
        ColorFlyoutCloseRequested?.Invoke();
    }

    /// <summary>Raised when the flyout should close after a custom color was applied.</summary>
    public event Action? ColorFlyoutCloseRequested;

    /// <summary>Goes back from the custom picker to the swatches without saving.</summary>
    [RelayCommand]
    public void CancelCustomColor() => IsCustomColorOpen = false;

    /// <summary>Starts listening for a shortcut.</summary>
    public void BeginRecording()
    {
        if (IsRecording)
        {
            return;
        }

        LiveText = string.Empty;
        ShortcutWarning = string.Empty;
        IsRecording = true;
        _hotkeys.Suspend(); // registered combos would otherwise be swallowed by Windows instead of reaching the recorder
    }

    /// <summary>Stops listening without changing the shortcut.</summary>
    public void EndRecording()
    {
        var wasRecording = IsRecording;
        IsRecording = false;
        LiveText = string.Empty;
        ShortcutWarning = string.Empty;
        if (wasRecording)
        {
            _hotkeys.Resume();
        }
    }

    /// <summary>
    /// Handles a key press while recording. Returns true when the key was consumed; <paramref name="finished"/>
    /// is true when recording ended (the view then drops focus). Tab is not consumed so focus can move on.
    /// </summary>
    public bool HandleKeyDown(Key key, KeyModifiers modifiers, out bool finished)
    {
        finished = false;
        if (!IsRecording)
        {
            return false;
        }

        if (key == Key.Tab)
        {
            EndRecording();
            return false;
        }

        var mods = SettingsShortcutKeys.Modifiers(modifiers);
        var held = mods.Count > 0 ? string.Join('+', mods) + "+" : string.Empty;

        if (key == Key.Escape)
        {
            EndRecording();
            finished = true;
            return true;
        }

        if (key == Key.Back && mods.Count == 0)
        {
            SetShortcut(string.Empty);
            EndRecording();
            finished = true;
            return true;
        }

        if (SettingsShortcutKeys.IsModifier(key))
        {
            LiveText = held;
            ShortcutWarning = string.Empty;
            return true;
        }

        var name = SettingsShortcutKeys.KeyName(key);
        if (name is null)
        {
            LiveText = held;
            ShortcutWarning = Loc.T("settings.stopwatch.keyUnsupported");
            return true;
        }

        var combo = held + name;
        if (mods.Count == 0 && !SettingsShortcutKeys.IsFunctionKey(name))
        {
            LiveText = combo;
            ShortcutWarning = Loc.T("settings.stopwatch.needModifier");
            return true;
        }

        if (_owner.FindShortcutOwner(this, combo) is { } clash)
        {
            LiveText = combo;
            ShortcutWarning = Loc.T("settings.stopwatch.alreadyUsed", ("name", clash.DisplayName));
            return true;
        }

        SetShortcut(combo);
        EndRecording();
        finished = true;
        return true;
    }

    /// <summary>Updates the live combo as modifiers are released.</summary>
    public void HandleKeyUp(KeyModifiers modifiers)
    {
        if (!IsRecording || ShortcutWarning.Length > 0)
        {
            return;
        }

        var mods = SettingsShortcutKeys.Modifiers(modifiers);
        LiveText = mods.Count > 0 ? string.Join('+', mods) + "+" : string.Empty;
    }

    private void SetShortcut(string value)
    {
        Shortcut = value;
        _owner.Save(this);
    }

    private bool IsNamed => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(this, 1);

    [RelayCommand]
    private void Delete() => _owner.ConfirmDelete(this);

    private static IBrush ParseBrush(string hex) =>
        Avalonia.Media.Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
}
