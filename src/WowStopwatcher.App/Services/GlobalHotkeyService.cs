using System.Diagnostics;
using System.Runtime.InteropServices;
using WowStopwatcher.Core;

namespace WowStopwatcher.App.Services;

/// <summary>
/// Registers the stopwatch shortcuts as system-wide hotkeys on a dedicated thread with its own message loop
/// (RegisterHotKey is bound to the thread that calls it). A hotkey toggles its stopwatch on the engine.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;

    private readonly StopwatchEngine _engine;
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private List<(string Id, ShortcutKey Key)> _pending = [];
    private string _signature = "\0";
    private volatile IReadOnlyList<string> _failedIds = [];
    private int _suspendCount;
    private bool _disposed;

    // Only touched on the hotkey thread.
    private readonly Dictionary<int, string> _registered = [];

    public GlobalHotkeyService(StopwatchEngine engine) => _engine = engine;

    /// <summary>Stopwatch ids whose shortcut could not be registered (for example taken by another app).</summary>
    public IReadOnlyList<string> FailedIds => _failedIds;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "GlobalHotkeys" };
        _thread.Start();
        _ready.Wait();

        _engine.Changed += OnEngineChanged;
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
        }

        PostThreadMessage(_threadId, WmApp, UIntPtr.Zero, IntPtr.Zero);
    }

    private void OnEngineChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Re-registers the hotkeys when the set of (id, shortcut) pairs changed.</summary>
    private void Refresh()
    {
        var wanted = new List<(string Id, ShortcutKey Key)>();
        foreach (var stopwatch in _engine.GetSnapshot().Stopwatches)
        {
            if (ShortcutParser.TryParse(stopwatch.Shortcut, out var key))
            {
                wanted.Add((stopwatch.Id, key));
            }
        }

        var signature = string.Join('|', wanted.Select(w => $"{w.Id}:{w.Key.Modifiers}:{w.Key.VirtualKey}"));
        lock (_lock)
        {
            if (_disposed || signature == _signature)
            {
                return;
            }

            _signature = signature;
            _pending = wanted;
        }

        PostThreadMessage(_threadId, WmApp, UIntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        // Touching the message queue creates it, so messages posted to this thread id are not lost.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _threadId = GetCurrentThreadId();
        _ready.Set();

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.Message == WmHotkey)
                {
                    if (_registered.TryGetValue((int)msg.WParam, out var id))
                    {
                        _engine.Toggle(id);
                    }
                }
                else if (msg.Message == WmApp)
                {
                    Apply();
                }
            }
        }
        finally
        {
            UnregisterAll();
        }
    }

    private void Apply()
    {
        List<(string Id, ShortcutKey Key)> wanted;
        bool suspended;
        lock (_lock)
        {
            wanted = _pending;
            suspended = _suspendCount > 0;
        }

        UnregisterAll();
        if (suspended)
        {
            _failedIds = [];
            return;
        }

        var failed = new List<string>();
        var nextId = 1;
        foreach (var (id, key) in wanted)
        {
            var hotkeyId = nextId++;
            if (RegisterHotKey(IntPtr.Zero, hotkeyId, key.Modifiers | ModNoRepeat, key.VirtualKey))
            {
                _registered[hotkeyId] = id;
            }
            else
            {
                failed.Add(id);
                Debug.WriteLine(
                    $"Global hotkey for stopwatch {id} could not be registered (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }

        _failedIds = failed;
    }

    private void UnregisterAll()
    {
        foreach (var hotkeyId in _registered.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, hotkeyId);
        }

        _registered.Clear();
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

        _engine.Changed -= OnEngineChanged;
        if (_thread is { IsAlive: true })
        {
            PostThreadMessage(_threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
