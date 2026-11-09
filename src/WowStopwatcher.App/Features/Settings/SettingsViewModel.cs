using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Navigation;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>The Settings page: general items and footer items contributed by features, stopwatch editing (saved immediately) and the version.</summary>
public sealed partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private const string NewStopwatchName = "New stopwatch";

    private readonly IStopwatchMaintenance _maintenance;
    private readonly IStopwatchRepository _stopwatches;
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;
    private readonly IDialogService _dialog;
    private readonly IShortcutService _hotkeys;
    private readonly IAppVersion _version;
    private readonly IUiDispatcher _dispatcher;
    private readonly StopwatchCapabilities _capabilities;

    public SettingsViewModel(
        IStopwatchMaintenance maintenance,
        IStopwatchRepository stopwatches,
        ISessionRepository sessions,
        TimeProvider time,
        IDialogService dialog,
        IShortcutService hotkeys,
        IAppVersion version,
        IUiDispatcher dispatcher,
        StopwatchCapabilities capabilities,
        IEnumerable<ISettingsGeneralItem> generalItems,
        IEnumerable<ISettingsFooterItem> footerItems)
    {
        _version = version;
        _dispatcher = dispatcher;
        _capabilities = capabilities;
        GeneralItems = [.. generalItems.OrderBy(i => i.Order)];
        FooterItems = [.. footerItems.OrderBy(i => i.Order)];
        _maintenance = maintenance;
        _stopwatches = stopwatches;
        _sessions = sessions;
        _time = time;
        _dialog = dialog;
        _hotkeys = hotkeys;

        foreach (var definition in _stopwatches.GetAll())
        {
            Items.Add(new SettingsStopwatchViewModel(this, definition, _hotkeys, _capabilities));
        }

        _hotkeys.FailuresChanged += OnHotkeyFailuresChanged;
        UpdateShortcutFailures();
    }

    public string Title => "Settings";

    public ObservableCollection<SettingsStopwatchViewModel> Items { get; } = [];

    /// <summary>Cards of the "General" section, contributed by features and ordered.</summary>
    public IReadOnlyList<ISettingsGeneralItem> GeneralItems { get; }

    public bool HasGeneralItems => GeneralItems.Count > 0;

    /// <summary>Items in the right-hand side of the footer, contributed by features and ordered.</summary>
    public IReadOnlyList<ISettingsFooterItem> FooterItems { get; }

    public bool HasFooterItems => FooterItems.Count > 0;

    /// <summary>True when the Hotkeys feature is present but global shortcuts cannot work in this session; the page then shows a notice.</summary>
    public bool AreShortcutsUnsupported => _hotkeys.IsAvailable && !_hotkeys.IsSupported;

    public string ShortcutsUnsupportedText => OperatingSystem.IsLinux()
        ? "Global shortcuts aren't available in this session (Wayland). They work on X11."
        : "Global shortcuts aren't available on this system.";

    public string VersionText => _version.DisplayText;

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

        var item = new SettingsStopwatchViewModel(this, definition, _hotkeys, _capabilities) { FocusNameRequested = true };
        Items.Add(item);
        SaveOrder();
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
        _maintenance.ReloadDefinitions();
    }

    /// <summary>
    /// Saves a change of the independent flag and tells the engine right away, so a stopwatch that is running
    /// (or started by a shortcut or the tray while Settings is open) follows its new type.
    /// </summary>
    internal void SaveType(SettingsStopwatchViewModel item)
    {
        Save(item);
        _maintenance.ReloadDefinitions();
    }

    /// <summary>The other stopwatch that already uses <paramref name="shortcut"/>, if any.</summary>
    internal SettingsStopwatchViewModel? FindShortcutOwner(SettingsStopwatchViewModel item, string shortcut) =>
        Items.FirstOrDefault(o => o != item && string.Equals(o.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));

    public void OnNavigatedTo()
    {
    }

    /// <summary>Leaving the page ends shortcut recording, stops listening to hotkey results and reloads the stopwatch definitions so changes show up.</summary>
    public void OnNavigatedFrom()
    {
        StopRecording();
        Detach();
        _maintenance.ReloadDefinitions();
    }

    /// <summary>Stops listening to hotkey registration results (called when the page is left).</summary>
    internal void Detach() => _hotkeys.FailuresChanged -= OnHotkeyFailuresChanged;

    private void OnHotkeyFailuresChanged(object? sender, EventArgs e) =>
        _dispatcher.Post(UpdateShortcutFailures);

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
}
