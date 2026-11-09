using WowStopwatcher.App.Composition;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Features.Hotkeys;

/// <summary>
/// Registers the stopwatch shortcuts as system-wide hotkeys through the backend of the current operating system
/// (Win32 on Windows, Carbon on macOS, X11 on Linux). A hotkey toggles its stopwatch on the engine.
/// </summary>
internal sealed class GlobalHotkeyService : IAppService, IShortcutService
{
    private readonly IStopwatchState _state;
    private readonly IStopwatchControl _control;
    private readonly IHotkeyBackend _backend;
    private readonly object _lock = new();
    private List<(string Id, ShortcutKey Key)> _wanted = [];
    private string _signature = "\0";
    private int _suspendCount;
    private bool _disposed;

    public GlobalHotkeyService(IStopwatchState state, IStopwatchControl control, IHotkeyBackend backend)
    {
        _state = state;
        _control = control;
        _backend = backend;
    }

    public bool IsAvailable => true;

    /// <summary>False when global hotkeys cannot work in this session (for example Linux on Wayland).</summary>
    public bool IsSupported => _backend.IsSupported;

    /// <summary>
    /// Stopwatch ids whose shortcut could not be registered (for example taken by another app).
    /// When hotkeys are not supported, every stopwatch with a shortcut is listed.
    /// </summary>
    public IReadOnlyList<string> FailedIds =>
        _backend.IsSupported ? _backend.FailedIds : _wanted.Select(w => w.Id).ToList();

    /// <summary>Raised, on any thread, after a backend finished applying the hotkeys and <see cref="FailedIds"/> may have changed.</summary>
    public event EventHandler? FailuresChanged;

    public void Start()
    {
        _backend.FailuresChanged += OnBackendFailuresChanged;
        _backend.Start(_control.Toggle);
        _state.Changed += OnEngineChanged;
        Refresh();
    }

    /// <summary>
    /// Unregisters all hotkeys (for example while a shortcut is being recorded). Calls are counted: hotkeys come back
    /// after as many <see cref="Resume"/> calls.
    /// </summary>
    public void Suspend() => ChangeSuspension(1);

    /// <summary>Undoes one <see cref="Suspend"/> and re-registers the current shortcuts once none is left. Extra calls are ignored.</summary>
    public void Resume() => ChangeSuspension(-1);

    private void ChangeSuspension(int delta)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _suspendCount = Math.Max(0, _suspendCount + delta);
            Push();
        }
    }

    private void OnBackendFailuresChanged() => FailuresChanged?.Invoke(this, EventArgs.Empty);

    private void OnEngineChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Re-reads the shortcuts from the engine and re-registers the hotkeys when they changed.</summary>
    private void Refresh()
    {
        var wanted = new List<(string Id, ShortcutKey Key)>();
        foreach (var stopwatch in _state.GetSnapshot().Stopwatches)
        {
            if (ShortcutParser.TryParse(stopwatch.Shortcut, out var key))
            {
                wanted.Add((stopwatch.Id, key));
            }
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _wanted = wanted;
            Push();
        }
    }

    /// <summary>Sends the effective hotkeys (none while suspended) to the backend when they differ from the last push. Call under the lock.</summary>
    private void Push()
    {
        var effective = _suspendCount > 0 ? [] : _wanted;
        var signature = string.Join('|', effective.Select(w => $"{w.Id}:{w.Key.Modifiers}:{w.Key.Key}"));
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        _backend.SetHotkeys(effective);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _state.Changed -= OnEngineChanged;
        _backend.FailuresChanged -= OnBackendFailuresChanged;
        _backend.Dispose();
    }
}
