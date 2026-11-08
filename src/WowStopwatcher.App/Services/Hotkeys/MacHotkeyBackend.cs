using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace WowStopwatcher.App.Services.Hotkeys;

/// <summary>
/// Registers hotkeys with the Carbon Event Manager (RegisterEventHotKey). Carbon hotkeys need no accessibility
/// permission. Everything runs on the UI thread, whose run loop delivers the hotkey events.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacHotkeyBackend : IHotkeyBackend
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // CarbonEvents.h / Events.h constants.
    private const uint KEventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint KEventHotKeyPressed = 5;
    private const uint KEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'
    private const uint HotKeySignature = 0x57535450; // 'WSTP', our own OSType
    private const int NoErr = 0;
    private const int EventNotHandledErr = -9874;

    // Events.h modifier masks used by RegisterEventHotKey.
    private const uint CmdKey = 0x0100; // cmdKey
    private const uint ShiftKey = 0x0200; // shiftKey
    private const uint OptionKey = 0x0800; // optionKey
    private const uint ControlKey = 0x1000; // controlKey

    private readonly object _lock = new();
    private readonly Dictionary<uint, (IntPtr Ref, string Id)> _registered = [];
    private readonly EventHandlerProc _handler; // field keeps the delegate alive for as long as Carbon may call it
    private Action<string> _onPressed = _ => { };
    private IntPtr _handlerRef;
    private IReadOnlyList<(string Id, ShortcutKey Key)> _pending = [];
    private volatile IReadOnlyList<string> _failedIds = [];
    private bool _disposed;

    public MacHotkeyBackend() => _handler = OnHotKeyEvent;

    public bool IsSupported => true;

    public IReadOnlyList<string> FailedIds => _failedIds;

    public event Action? FailuresChanged;

    public void Start(Action<string> onPressed)
    {
        _onPressed = onPressed;
        Dispatcher.UIThread.Invoke(() =>
        {
            var spec = new EventTypeSpec { EventClass = KEventClassKeyboard, EventKind = KEventHotKeyPressed };
            var status = InstallEventHandler(GetApplicationEventTarget(), _handler, 1, [spec], IntPtr.Zero, out _handlerRef);
            if (status != NoErr)
            {
                Debug.WriteLine($"InstallEventHandler failed (OSStatus {status}); global hotkeys will not fire.");
                _handlerRef = IntPtr.Zero;
            }
        });
    }

    public void SetHotkeys(IReadOnlyList<(string Id, ShortcutKey Key)> hotkeys)
    {
        lock (_lock)
        {
            _pending = hotkeys;
        }

        Dispatcher.UIThread.Post(Apply);
    }

    private void Apply()
    {
        IReadOnlyList<(string Id, ShortcutKey Key)> wanted;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            wanted = _pending;
        }

        UnregisterAll();
        var failed = new List<string>();
        uint nextId = 1;
        foreach (var (id, key) in wanted)
        {
            var hotkeyId = nextId++;
            if (!TryGetKeyCode(key.Key, out var keyCode))
            {
                failed.Add(id); // for example F21-F24, which Mac keyboards do not have
                continue;
            }

            var status = RegisterEventHotKey(
                keyCode,
                ToCarbonModifiers(key.Modifiers),
                new EventHotKeyId { Signature = HotKeySignature, Id = hotkeyId },
                GetApplicationEventTarget(),
                0,
                out var hotKeyRef);
            if (status == NoErr)
            {
                _registered[hotkeyId] = (hotKeyRef, id);
            }
            else
            {
                failed.Add(id);
                Debug.WriteLine($"Global hotkey for stopwatch {id} could not be registered (OSStatus {status}).");
            }
        }

        _failedIds = failed;
        FailuresChanged?.Invoke();
    }

    private void UnregisterAll()
    {
        foreach (var (hotKeyRef, _) in _registered.Values)
        {
            UnregisterEventHotKey(hotKeyRef);
        }

        _registered.Clear();
    }

    private int OnHotKeyEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        var status = GetEventParameter(
            theEvent, KEventParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
            (nuint)Marshal.SizeOf<EventHotKeyId>(), IntPtr.Zero, out EventHotKeyId hotKeyId);
        if (status != NoErr || hotKeyId.Signature != HotKeySignature
            || !_registered.TryGetValue(hotKeyId.Id, out var entry))
        {
            return EventNotHandledErr;
        }

        _onPressed(entry.Id);
        return NoErr;
    }

    private static uint ToCarbonModifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            result |= ControlKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            result |= OptionKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            result |= ShiftKey;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            result |= CmdKey;
        }

        return result;
    }

    /// <summary>Virtual key codes from HIToolbox/Events.h (kVK_ANSI_*, kVK_F*, kVK_ANSI_Keypad*, ...). They are layout-independent positions.</summary>
    private static bool TryGetKeyCode(HotkeyKey key, out uint code)
    {
        int value = key switch
        {
            HotkeyKey.A => 0x00, // kVK_ANSI_A
            HotkeyKey.S => 0x01, // kVK_ANSI_S
            HotkeyKey.D => 0x02, // kVK_ANSI_D
            HotkeyKey.F => 0x03, // kVK_ANSI_F
            HotkeyKey.H => 0x04, // kVK_ANSI_H
            HotkeyKey.G => 0x05, // kVK_ANSI_G
            HotkeyKey.Z => 0x06, // kVK_ANSI_Z
            HotkeyKey.X => 0x07, // kVK_ANSI_X
            HotkeyKey.C => 0x08, // kVK_ANSI_C
            HotkeyKey.V => 0x09, // kVK_ANSI_V
            HotkeyKey.B => 0x0B, // kVK_ANSI_B
            HotkeyKey.Q => 0x0C, // kVK_ANSI_Q
            HotkeyKey.W => 0x0D, // kVK_ANSI_W
            HotkeyKey.E => 0x0E, // kVK_ANSI_E
            HotkeyKey.R => 0x0F, // kVK_ANSI_R
            HotkeyKey.Y => 0x10, // kVK_ANSI_Y
            HotkeyKey.T => 0x11, // kVK_ANSI_T
            HotkeyKey.O => 0x1F, // kVK_ANSI_O
            HotkeyKey.U => 0x20, // kVK_ANSI_U
            HotkeyKey.I => 0x22, // kVK_ANSI_I
            HotkeyKey.P => 0x23, // kVK_ANSI_P
            HotkeyKey.L => 0x25, // kVK_ANSI_L
            HotkeyKey.J => 0x26, // kVK_ANSI_J
            HotkeyKey.K => 0x28, // kVK_ANSI_K
            HotkeyKey.N => 0x2D, // kVK_ANSI_N
            HotkeyKey.M => 0x2E, // kVK_ANSI_M

            HotkeyKey.D1 => 0x12, // kVK_ANSI_1
            HotkeyKey.D2 => 0x13, // kVK_ANSI_2
            HotkeyKey.D3 => 0x14, // kVK_ANSI_3
            HotkeyKey.D4 => 0x15, // kVK_ANSI_4
            HotkeyKey.D6 => 0x16, // kVK_ANSI_6
            HotkeyKey.D5 => 0x17, // kVK_ANSI_5
            HotkeyKey.D9 => 0x19, // kVK_ANSI_9
            HotkeyKey.D7 => 0x1A, // kVK_ANSI_7
            HotkeyKey.D8 => 0x1C, // kVK_ANSI_8
            HotkeyKey.D0 => 0x1D, // kVK_ANSI_0

            HotkeyKey.F1 => 0x7A, // kVK_F1
            HotkeyKey.F2 => 0x78, // kVK_F2
            HotkeyKey.F3 => 0x63, // kVK_F3
            HotkeyKey.F4 => 0x76, // kVK_F4
            HotkeyKey.F5 => 0x60, // kVK_F5
            HotkeyKey.F6 => 0x61, // kVK_F6
            HotkeyKey.F7 => 0x62, // kVK_F7
            HotkeyKey.F8 => 0x64, // kVK_F8
            HotkeyKey.F9 => 0x65, // kVK_F9
            HotkeyKey.F10 => 0x6D, // kVK_F10
            HotkeyKey.F11 => 0x67, // kVK_F11
            HotkeyKey.F12 => 0x6F, // kVK_F12
            HotkeyKey.F13 => 0x69, // kVK_F13
            HotkeyKey.F14 => 0x6B, // kVK_F14
            HotkeyKey.F15 => 0x71, // kVK_F15
            HotkeyKey.F16 => 0x6A, // kVK_F16
            HotkeyKey.F17 => 0x40, // kVK_F17
            HotkeyKey.F18 => 0x4F, // kVK_F18
            HotkeyKey.F19 => 0x50, // kVK_F19
            HotkeyKey.F20 => 0x5A, // kVK_F20

            HotkeyKey.NumPad0 => 0x52, // kVK_ANSI_Keypad0
            HotkeyKey.NumPad1 => 0x53, // kVK_ANSI_Keypad1
            HotkeyKey.NumPad2 => 0x54, // kVK_ANSI_Keypad2
            HotkeyKey.NumPad3 => 0x55, // kVK_ANSI_Keypad3
            HotkeyKey.NumPad4 => 0x56, // kVK_ANSI_Keypad4
            HotkeyKey.NumPad5 => 0x57, // kVK_ANSI_Keypad5
            HotkeyKey.NumPad6 => 0x58, // kVK_ANSI_Keypad6
            HotkeyKey.NumPad7 => 0x59, // kVK_ANSI_Keypad7
            HotkeyKey.NumPad8 => 0x5B, // kVK_ANSI_Keypad8
            HotkeyKey.NumPad9 => 0x5C, // kVK_ANSI_Keypad9

            HotkeyKey.Space => 0x31, // kVK_Space
            HotkeyKey.PageUp => 0x74, // kVK_PageUp
            HotkeyKey.PageDown => 0x79, // kVK_PageDown
            HotkeyKey.End => 0x77, // kVK_End
            HotkeyKey.Home => 0x73, // kVK_Home
            HotkeyKey.Left => 0x7B, // kVK_LeftArrow
            HotkeyKey.Up => 0x7E, // kVK_UpArrow
            HotkeyKey.Right => 0x7C, // kVK_RightArrow
            HotkeyKey.Down => 0x7D, // kVK_DownArrow
            HotkeyKey.Insert => 0x72, // kVK_Help, the key in the Insert position on Apple keyboards
            HotkeyKey.Delete => 0x75, // kVK_ForwardDelete
            _ => -1, // F21-F24 do not exist on Mac keyboards
        };
        code = (uint)Math.Max(value, 0);
        return value >= 0;
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

        Dispatcher.UIThread.Invoke(() =>
        {
            UnregisterAll();
            if (_handlerRef != IntPtr.Zero)
            {
                RemoveEventHandler(_handlerRef);
                _handlerRef = IntPtr.Zero;
            }
        });
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target, EventHandlerProc handler, uint numTypes, EventTypeSpec[] typeList, IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyId hotKeyId, IntPtr target, uint options, out IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(
        IntPtr theEvent, uint name, uint desiredType, IntPtr actualType, nuint bufferSize, IntPtr actualSize, out EventHotKeyId data);
}
