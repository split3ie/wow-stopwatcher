using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WowStopwatcher.App.Services.Hotkeys;

/// <summary>
/// Registers hotkeys with Win32 RegisterHotKey on a dedicated thread with its own message loop
/// (RegisterHotKey is bound to the thread that calls it).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeyBackend : IHotkeyBackend
{
    private const uint WmHotkey = 0x0312;
    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;

    private readonly object _lock = new();
    private readonly ManualResetEventSlim _ready = new();
    private Action<string> _onPressed = _ => { };
    private Thread? _thread;
    private uint _threadId;
    private IReadOnlyList<(string Id, ShortcutKey Key)> _pending = [];
    private volatile IReadOnlyList<string> _failedIds = [];

    // Only touched on the hotkey thread.
    private readonly Dictionary<int, string> _registered = [];

    public bool IsSupported => true;

    public event Action? FailuresChanged;

    public IReadOnlyList<string> FailedIds => _failedIds;

    public void Start(Action<string> onPressed)
    {
        _onPressed = onPressed;
        _thread = new Thread(Run) { IsBackground = true, Name = "GlobalHotkeys" };
        _thread.Start();
        _ready.Wait();
    }

    public void SetHotkeys(IReadOnlyList<(string Id, ShortcutKey Key)> hotkeys)
    {
        lock (_lock)
        {
            _pending = hotkeys;
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
                        _onPressed(id);
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
        IReadOnlyList<(string Id, ShortcutKey Key)> wanted;
        lock (_lock)
        {
            wanted = _pending;
        }

        UnregisterAll();
        var failed = new List<string>();
        var nextId = 1;
        foreach (var (id, key) in wanted)
        {
            var hotkeyId = nextId++;
            if (TryGetVirtualKey(key.Key, out var vk)
                && RegisterHotKey(IntPtr.Zero, hotkeyId, (uint)key.Modifiers | ModNoRepeat, vk))
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
        FailuresChanged?.Invoke();
    }

    private void UnregisterAll()
    {
        foreach (var hotkeyId in _registered.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, hotkeyId);
        }

        _registered.Clear();
    }

    /// <summary>Win32 virtual-key codes (WinUser.h VK_*). HotkeyModifiers values already equal MOD_*.</summary>
    private static bool TryGetVirtualKey(HotkeyKey key, out uint vk)
    {
        vk = key switch
        {
            >= HotkeyKey.A and <= HotkeyKey.Z => 0x41u + (uint)(key - HotkeyKey.A), // VK equals the ASCII code
            >= HotkeyKey.D0 and <= HotkeyKey.D9 => 0x30u + (uint)(key - HotkeyKey.D0),
            >= HotkeyKey.F1 and <= HotkeyKey.F24 => 0x70u + (uint)(key - HotkeyKey.F1), // VK_F1
            >= HotkeyKey.NumPad0 and <= HotkeyKey.NumPad9 => 0x60u + (uint)(key - HotkeyKey.NumPad0), // VK_NUMPAD0
            HotkeyKey.Space => 0x20,
            HotkeyKey.PageUp => 0x21,
            HotkeyKey.PageDown => 0x22,
            HotkeyKey.End => 0x23,
            HotkeyKey.Home => 0x24,
            HotkeyKey.Left => 0x25,
            HotkeyKey.Up => 0x26,
            HotkeyKey.Right => 0x27,
            HotkeyKey.Down => 0x28,
            HotkeyKey.Insert => 0x2D,
            HotkeyKey.Delete => 0x2E,
            _ => 0,
        };
        return vk != 0;
    }

    public void Dispose()
    {
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
