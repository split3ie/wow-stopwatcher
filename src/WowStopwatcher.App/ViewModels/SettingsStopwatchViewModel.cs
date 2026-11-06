using System.Collections.ObjectModel;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Services;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>One editable stopwatch card on the Settings page. Every change is saved immediately.</summary>
public sealed partial class SettingsStopwatchViewModel : ObservableObject
{
    private const string NoNameLabel = "Untitled";

    private readonly SettingsViewModel _owner;
    private readonly GlobalHotkeyService _hotkeys;
    private string _savedName;

    public SettingsStopwatchViewModel(SettingsViewModel owner, StopwatchDefinition definition, GlobalHotkeyService hotkeys)
    {
        _owner = owner;
        _hotkeys = hotkeys;
        Id = definition.Id;
        _name = definition.Name;
        _savedName = definition.Name;
        _color = definition.Color;
        _brush = ParseBrush(definition.Color);
        _hasGoal = definition.HasGoal;
        _goalText = definition.HasGoal ? TimeFormat.FormatGoal(definition.Goal!.Value) : SettingsGoalMask.Empty;
        _shortcut = definition.Shortcut ?? string.Empty;
        Swatches =
        [
            .. SettingsSwatchViewModel.Palette.Select(p => new SettingsSwatchViewModel(
                this, p.Name, p.Hex, string.Equals(p.Hex, definition.Color, StringComparison.OrdinalIgnoreCase))),
        ];
    }

    public string Id { get; }

    public ObservableCollection<SettingsSwatchViewModel> Swatches { get; }

    /// <summary>Set on a newly added card so the view focuses and selects its name once.</summary>
    public bool FocusNameRequested { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(SectionLabel), nameof(GripLabel), nameof(DeleteLabel), nameof(GoalLabel))]
    private string _name;

    [ObservableProperty]
    private string _color;

    [ObservableProperty]
    private IBrush _brush;

    [ObservableProperty]
    private bool _hasGoal;

    /// <summary>Goal as "hh:mm:ss". Kept when the goal checkbox is cleared.</summary>
    [ObservableProperty]
    private string _goalText;

    /// <summary>Stored shortcut text, empty when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText))]
    private string _shortcut;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText), nameof(ShortcutPlaceholder), nameof(ShortcutHint))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutText))]
    private string _liveText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutHint), nameof(HasShortcutWarning))]
    private string _shortcutWarning = string.Empty;

    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isDropTarget;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? NoNameLabel : Name;

    public string SectionLabel => DisplayName + " stopwatch";

    public string GripLabel => $"Reorder {LabelName("stopwatch")}. Drag, or use the up and down arrow keys";

    public string DeleteLabel => $"Delete {LabelName("stopwatch")}";

    public string GoalLabel => $"{LabelName("Stopwatch")} goal time, hours minutes seconds";

    public string ShortcutText => IsRecording ? LiveText : Shortcut;

    public string ShortcutPlaceholder => IsRecording ? "Press keys…" : "Not set";

    public bool HasShortcutWarning => ShortcutWarning.Length > 0;

    public string ShortcutHint => ShortcutWarning.Length > 0
        ? ShortcutWarning
        : IsRecording ? "Esc to cancel · Backspace to clear" : string.Empty;

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
            ShortcutWarning = "Key not supported";
            return true;
        }

        var combo = held + name;
        if (mods.Count == 0 && !SettingsShortcutKeys.IsFunctionKey(name))
        {
            LiveText = combo;
            ShortcutWarning = "Add Ctrl, Alt or Shift";
            return true;
        }

        if (_owner.FindShortcutOwner(this, combo) is { } clash)
        {
            LiveText = combo;
            ShortcutWarning = "Already used by " + clash.DisplayName;
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

    private string LabelName(string fallback) => string.IsNullOrWhiteSpace(Name) ? fallback : Name;

    [RelayCommand]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.Move(this, 1);

    [RelayCommand]
    private void Delete() => _owner.ConfirmDelete(this);

    private static IBrush ParseBrush(string hex) =>
        Avalonia.Media.Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
}
