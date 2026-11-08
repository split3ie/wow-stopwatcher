using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Notifications;
using WowStopwatcher.App.Services;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.ViewModels;

/// <summary>The Settings page: autostart, stopwatch editing (saved immediately), version and update check.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string NewStopwatchName = "New stopwatch";

    private readonly StopwatchEngine _engine;
    private readonly IStopwatchRepository _stopwatches;
    private readonly ISessionRepository _sessions;
    private readonly ISettingsRepository _settings;
    private readonly TimeProvider _time;
    private readonly ConfirmDialogViewModel _dialog;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly AutostartService _autostart = new();
    private readonly UpdateChecker _updateChecker = new();
    private bool _syncingAutostart;

    public SettingsViewModel(
        StopwatchEngine engine,
        IStopwatchRepository stopwatches,
        ISessionRepository sessions,
        ISettingsRepository settings,
        TimeProvider time,
        ConfirmDialogViewModel dialog,
        GlobalHotkeyService hotkeys)
    {
        _settings = settings;
        _goalNotificationsEnabled = GoalNotificationSetting.IsEnabled(settings);
        _engine = engine;
        _stopwatches = stopwatches;
        _sessions = sessions;
        _time = time;
        _dialog = dialog;
        _hotkeys = hotkeys;

        foreach (var definition in _stopwatches.GetAll())
        {
            Items.Add(new SettingsStopwatchViewModel(this, definition, _hotkeys));
        }

        _hotkeys.FailuresChanged += OnHotkeyFailuresChanged;
        UpdateShortcutFailures();

        IsAutostartSupported = _autostart.IsSupported;
        if (IsAutostartSupported)
        {
            try
            {
                _autostartEnabled = _autostart.IsEnabled();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AutostartError = "Couldn't read the autostart setting";
            }
        }
    }

    public string Title => "Settings";

    public ObservableCollection<SettingsStopwatchViewModel> Items { get; } = [];

    public bool IsAutostartSupported { get; }

    /// <summary>True when global shortcuts cannot work in this session; the page then shows a notice.</summary>
    public bool AreShortcutsUnsupported => !_hotkeys.IsSupported;

    public string ShortcutsUnsupportedText => OperatingSystem.IsLinux()
        ? "Global shortcuts aren't available in this session (Wayland). They work on X11."
        : "Global shortcuts aren't available on this system.";

    /// <summary>Version of the running app without any "+commit" suffix, for example "0.1.0".</summary>
    public static string CurrentVersion { get; } = ReadVersion();

    public string VersionText => "v" + CurrentVersion;

    [ObservableProperty]
    private bool _autostartEnabled;

    [ObservableProperty]
    private string _autostartError = string.Empty;

    [ObservableProperty]
    private bool _goalNotificationsEnabled;

    partial void OnGoalNotificationsEnabledChanged(bool value) =>
        GoalNotificationSetting.SetEnabled(_settings, value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    private bool _isBusy;

    public bool IsIdle => !IsBusy;

    /// <summary>Result text of the last update check (empty before the first check).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateMessage))]
    private string _updateMessage = string.Empty;

    /// <summary>Release page of an available update; when set the message is shown as a link.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateMessage), nameof(HasUpdateLink))]
    private string? _updateUrl;

    [ObservableProperty]
    private bool _isUpdateOk;

    [ObservableProperty]
    private bool _isUpdateError;

    public bool HasUpdateLink => UpdateUrl is not null;

    /// <summary>True when the message is plain text (not a link).</summary>
    public bool HasUpdateMessage => UpdateMessage.Length > 0 && UpdateUrl is null;

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_syncingAutostart)
        {
            return;
        }

        try
        {
            _autostart.SetEnabled(value);
            AutostartError = string.Empty;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException
            or InvalidOperationException)
        {
            // Show the real state again instead of a switch that does nothing.
            _syncingAutostart = true;
            AutostartEnabled = !value;
            _syncingAutostart = false;
            AutostartError = "Couldn't change the autostart setting";
        }
    }

    [RelayCommand]
    private void AddStopwatch()
    {
        var used = Items.Select(i => i.Color).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var palette = SettingsSwatchViewModel.Palette;
        var color = palette.FirstOrDefault(p => !used.Contains(p.Hex)).Hex
            ?? palette[Items.Count % palette.Count].Hex;

        var definition = new StopwatchDefinition(
            StopwatchDefinition.NewId(), NewStopwatchName, color, null,
            DefaultShortcuts.Next(Items.Select(i => i.StoredShortcut)), Items.Count,
            ActiveSinceUtc: _time.GetUtcNow());
        _stopwatches.Add(definition);

        var item = new SettingsStopwatchViewModel(this, definition, _hotkeys) { FocusNameRequested = true };
        Items.Add(item);
        SaveOrder();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        IsUpdateOk = false;
        IsUpdateError = false;
        UpdateUrl = null;
        UpdateMessage = string.Empty;
        try
        {
            var result = await _updateChecker.CheckAsync(CurrentVersion);
            switch (result.Status)
            {
                case UpdateChecker.UpdateStatus.UpToDate:
                    UpdateMessage = "You’re on the latest version";
                    IsUpdateOk = true;
                    break;
                case UpdateChecker.UpdateStatus.Available when IsHttpsUrl(result.Url):
                    UpdateMessage = $"Version {result.Version} is available";
                    UpdateUrl = result.Url;
                    break;
                case UpdateChecker.UpdateStatus.NoReleases:
                    UpdateMessage = "No releases published yet";
                    break;
                default:
                    UpdateMessage = "Couldn’t check for updates";
                    IsUpdateError = true;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves the card's current values (name, color, goal, shortcut); the sort order is kept.</summary>
    internal void Save(SettingsStopwatchViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var stored = _stopwatches.Get(item.Id);
        var updated = stored is null
            ? new StopwatchDefinition(
                item.Id, item.SavedName, item.Color, item.Goal, item.StoredShortcut, index,
                IsIndependent: item.IsIndependent, IsPermanent: item.IsPermanent)
            : stored with
            {
                Name = item.SavedName,
                Color = item.Color,
                Goal = item.Goal,
                Shortcut = item.StoredShortcut,
                IsIndependent = item.IsIndependent,
                IsPermanent = item.IsPermanent,
            };
        _stopwatches.Update(updated);
    }

    /// <summary>Saves a change of the permanent flag and tells the engine so its definitions carry it.</summary>
    internal void SavePermanent(SettingsStopwatchViewModel item)
    {
        Save(item);
        _engine.ReloadDefinitions();
    }

    /// <summary>
    /// Saves a change of the independent flag and tells the engine right away, so a stopwatch that is running
    /// (or started by a shortcut or the tray while Settings is open) follows its new type.
    /// </summary>
    internal void SaveType(SettingsStopwatchViewModel item)
    {
        Save(item);
        _engine.ReloadDefinitions();
    }

    /// <summary>The other stopwatch that already uses <paramref name="shortcut"/>, if any.</summary>
    internal SettingsStopwatchViewModel? FindShortcutOwner(SettingsStopwatchViewModel item, string shortcut) =>
        Items.FirstOrDefault(o => o != item && string.Equals(o.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stops listening to hotkey registration results (called when the page is left).</summary>
    internal void Detach() => _hotkeys.FailuresChanged -= OnHotkeyFailuresChanged;

    private void OnHotkeyFailuresChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateShortcutFailures);

    private void UpdateShortcutFailures()
    {
        var failed = _hotkeys.IsSupported ? _hotkeys.FailedIds.ToHashSet() : [];
        foreach (var item in Items)
        {
            item.IsShortcutTaken = failed.Contains(item.Id);
        }
    }

    /// <summary>Ends any shortcut recording so global hotkeys are active again (called when the page is left).</summary>
    internal void StopRecording()
    {
        foreach (var item in Items)
        {
            item.EndRecording();
        }
    }

    internal void Move(SettingsStopwatchViewModel item, int delta)
    {
        var from = Items.IndexOf(item);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Items.Count)
        {
            return;
        }

        Items.Move(from, to);
        SaveOrder();
    }

    /// <summary>Moves a dragged card to the position of the target card and saves the new order.</summary>
    public void MoveTo(SettingsStopwatchViewModel item, SettingsStopwatchViewModel target)
    {
        var from = Items.IndexOf(item);
        var to = Items.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
        {
            return;
        }

        Items.Move(from, to);
        SaveOrder();
    }

    internal void ConfirmDelete(SettingsStopwatchViewModel item)
    {
        var title = string.IsNullOrWhiteSpace(item.Name) ? "Delete this stopwatch?" : $"Delete {item.Name}?";
        _dialog.Show(
            title,
            "The stopwatch, its goal and its shortcut are removed. Statistics already logged for it are kept.",
            "Delete",
            () =>
            {
                item.EndRecording();
                _stopwatches.SoftDelete(item.Id);
                Items.Remove(item);
                SaveOrder();
            });
    }

    private void SaveOrder()
    {
        // Deleted stopwatches keep a slot after the visible ones so sort orders stay unique.
        var deleted = _stopwatches.GetAll(includeDeleted: true).Where(s => s.IsDeleted).Select(s => s.Id);
        _stopwatches.SaveSortOrder([.. Items.Select(i => i.Id), .. deleted]);
    }

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsViewModel).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        var plus = text.IndexOf('+');
        return plus >= 0 ? text[..plus] : text;
    }
}
