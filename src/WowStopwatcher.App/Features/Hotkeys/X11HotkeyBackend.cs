using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WowStopwatcher.App.Features.Hotkeys;

/// <summary>
/// Registers hotkeys as passive key grabs (XGrabKey) on the X11 root window. Works on X11 sessions only: Wayland has no
/// global key grabs, so <see cref="TryCreate"/> returns null there. All Xlib calls on the backend's own display connection
/// happen on one background thread, which polls for events; registration changes are handed over through a flag.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11HotkeyBackend : IHotkeyBackend
{
    private const string LibX11 = "libX11.so.6";

    // X.h: event types, modifier masks and grab modes.
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1; // CapsLock
    private const uint ControlMask = 1 << 2;
    private const uint Mod1Mask = 1 << 3; // Alt
    private const uint Mod2Mask = 1 << 4; // NumLock
    private const uint Mod4Mask = 1 << 6; // Super
    private const uint RelevantMask = ShiftMask | ControlMask | Mod1Mask | Mod4Mask;
    private const int GrabModeAsync = 1;

    /// <summary>Lock-key states a hotkey must also be grabbed with, otherwise CapsLock or NumLock would disable it.</summary>
    private static readonly uint[] LockCombinations = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];

    // XSetErrorHandler is process-wide, so the handler is installed only around the grabs and the previous one restored.
    private static readonly XErrorHandler ErrorHandler = OnXError;
    private static volatile bool s_grabFailed;
    private static IntPtr s_errorDisplay;
    private static IntPtr s_previousHandler;

    private readonly IntPtr _display;
    private readonly object _lock = new();
    private readonly Dictionary<(uint KeyCode, uint State), string> _registered = [];
    private readonly List<(uint KeyCode, uint State)> _grabs = [];
    private readonly HashSet<uint> _down = [];
    private Action<string> _onPressed = _ => { };
    private Thread? _thread;
    private IReadOnlyList<(string Id, ShortcutKey Key)> _pending = [];
    private volatile IReadOnlyList<string> _failedIds = [];
    private volatile bool _dirty;
    private volatile bool _quit;
    private nuint _root;

    private X11HotkeyBackend(IntPtr display) => _display = display;

    public bool IsSupported => true;

    public IReadOnlyList<string> FailedIds => _failedIds;

    public event Action? FailuresChanged;

    /// <summary>Returns null when global hotkeys are not possible: Wayland session, no DISPLAY, no libX11 or no X server.</summary>
    public static X11HotkeyBackend? TryCreate()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return null;
        }

        try
        {
            XInitThreads();
            var display = XOpenDisplay(IntPtr.Zero);
            return display == IntPtr.Zero ? null : new X11HotkeyBackend(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine($"X11 is not available for global hotkeys: {ex.Message}");
            return null;
        }
    }

    public void Start(Action<string> onPressed)
    {
        _onPressed = onPressed;
        _root = XDefaultRootWindow(_display);

        // Without this, auto-repeat of a held key arrives as release/press pairs and would toggle repeatedly.
        XkbSetDetectableAutoRepeat(_display, 1, out _);

        _thread = new Thread(Run) { IsBackground = true, Name = "GlobalHotkeys" };
        _thread.Start();
    }

    public void SetHotkeys(IReadOnlyList<(string Id, ShortcutKey Key)> hotkeys)
    {
        lock (_lock)
        {
            _pending = hotkeys;
        }

        _dirty = true;
    }

    private void Run()
    {
        var buffer = Marshal.AllocHGlobal(256); // XEvent is a union of 24 longs
        try
        {
            while (!_quit)
            {
                if (_dirty)
                {
                    _dirty = false;
                    Apply();
                }

                while (!_quit && XPending(_display) > 0)
                {
                    XNextEvent(_display, buffer);
                    HandleEvent(buffer);
                }

                Thread.Sleep(20);
            }
        }
        finally
        {
            UngrabAll();
            XCloseDisplay(_display);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void HandleEvent(IntPtr buffer)
    {
        var type = Marshal.ReadInt32(buffer);
        if (type is not (KeyPress or KeyRelease))
        {
            return;
        }

        var e = Marshal.PtrToStructure<XKeyEvent>(buffer);
        if (type == KeyRelease)
        {
            _down.Remove(e.KeyCode);
        }
        else if (_down.Add(e.KeyCode)
                 && _registered.TryGetValue((e.KeyCode, e.State & RelevantMask), out var id))
        {
            _onPressed(id);
        }
    }

    private void Apply()
    {
        IReadOnlyList<(string Id, ShortcutKey Key)> wanted;
        lock (_lock)
        {
            wanted = _pending;
        }

        UngrabAll();
        var failed = new List<string>();
        foreach (var (id, key) in wanted)
        {
            var state = ToState(key.Modifiers);
            if (!TryGetKeySym(key.Key, out var keySym))
            {
                failed.Add(id);
                continue;
            }

            var keyCode = XKeysymToKeycode(_display, keySym);
            if (keyCode == 0 || _registered.ContainsKey((keyCode, state)) || !TryGrab(keyCode, state))
            {
                failed.Add(id);
                Debug.WriteLine($"Global hotkey for stopwatch {id} could not be registered (key unavailable or already grabbed).");
                continue;
            }

            _registered[(keyCode, state)] = id;
            _grabs.Add((keyCode, state));
        }

        _failedIds = failed;
        FailuresChanged?.Invoke();
    }

    /// <summary>Grabs the key with every lock-key combination. A BadAccess (key taken by another client) arrives asynchronously, so sync and check.</summary>
    private bool TryGrab(uint keyCode, uint state)
    {
        s_errorDisplay = _display;
        s_grabFailed = false;
        s_previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorHandler));
        try
        {
            foreach (var extra in LockCombinations)
            {
                XGrabKey(_display, (int)keyCode, state | extra, _root, 0, GrabModeAsync, GrabModeAsync);
            }

            XSync(_display, 0);
            if (!s_grabFailed)
            {
                return true;
            }

            foreach (var extra in LockCombinations)
            {
                XUngrabKey(_display, (int)keyCode, state | extra, _root);
            }

            XSync(_display, 0);
            return false;
        }
        finally
        {
            XSetErrorHandler(s_previousHandler);
        }
    }

    private void UngrabAll()
    {
        if (_grabs.Count > 0)
        {
            foreach (var (keyCode, state) in _grabs)
            {
                foreach (var extra in LockCombinations)
                {
                    XUngrabKey(_display, (int)keyCode, state | extra, _root);
                }
            }

            XSync(_display, 0);
        }

        _grabs.Clear();
        _registered.Clear();
        _down.Clear();
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        if (display == s_errorDisplay)
        {
            s_grabFailed = true;
            return 0;
        }

        // An error on another connection (the UI toolkit's): pass it on to the handler that was installed before ours.
        var previous = s_previousHandler;
        return previous == IntPtr.Zero
            ? 0
            : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, errorEvent);
    }

    private static uint ToState(HotkeyModifiers modifiers)
    {
        uint state = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            state |= ControlMask;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            state |= Mod1Mask;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            state |= ShiftMask;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            state |= Mod4Mask;
        }

        return state;
    }

    /// <summary>X11 keysyms (keysymdef.h): XK_a, XK_0, XK_F1, XK_KP_0, XK_space, XK_Prior, ...</summary>
    private static bool TryGetKeySym(HotkeyKey key, out nuint keySym)
    {
        keySym = key switch
        {
            >= HotkeyKey.A and <= HotkeyKey.Z => 0x61u + (nuint)(key - HotkeyKey.A), // XK_a (lowercase; keycode lookup is case-agnostic)
            >= HotkeyKey.D0 and <= HotkeyKey.D9 => 0x30u + (nuint)(key - HotkeyKey.D0), // XK_0
            >= HotkeyKey.F1 and <= HotkeyKey.F24 => 0xFFBEu + (nuint)(key - HotkeyKey.F1), // XK_F1
            >= HotkeyKey.NumPad0 and <= HotkeyKey.NumPad9 => 0xFFB0u + (nuint)(key - HotkeyKey.NumPad0), // XK_KP_0
            HotkeyKey.Space => 0x0020, // XK_space
            HotkeyKey.PageUp => 0xFF55, // XK_Page_Up
            HotkeyKey.PageDown => 0xFF56, // XK_Page_Down
            HotkeyKey.End => 0xFF57, // XK_End
            HotkeyKey.Home => 0xFF50, // XK_Home
            HotkeyKey.Left => 0xFF51, // XK_Left
            HotkeyKey.Up => 0xFF52, // XK_Up
            HotkeyKey.Right => 0xFF53, // XK_Right
            HotkeyKey.Down => 0xFF54, // XK_Down
            HotkeyKey.Insert => 0xFF63, // XK_Insert
            HotkeyKey.Delete => 0xFFFF, // XK_Delete
            _ => 0,
        };
        return keySym != 0;
    }

    public void Dispose()
    {
        _quit = true;
        if (_thread is { IsAlive: true })
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        else if (_thread is null)
        {
            XCloseDisplay(_display);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    /// <summary>XKeyEvent from Xlib.h; natural alignment matches the C layout on 32 and 64 bit.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint Root;
        public nuint SubWindow;
        public nuint Time;
        public int X;
        public int Y;
        public int XRoot;
        public int YRoot;
        public uint State;
        public uint KeyCode;
        public int SameScreen;
    }

    [DllImport(LibX11)]
    private static extern int XInitThreads();

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern nuint XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, nuint keySym);

    [DllImport(LibX11)]
    private static extern int XGrabKey(
        IntPtr display, int keyCode, uint modifiers, nuint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    private static extern int XUngrabKey(IntPtr display, int keyCode, uint modifiers, nuint grabWindow);

    [DllImport(LibX11)]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport(LibX11)]
    private static extern int XPending(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(LibX11)]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    [DllImport(LibX11)]
    private static extern int XkbSetDetectableAutoRepeat(IntPtr display, int detectable, out int supported);
}
