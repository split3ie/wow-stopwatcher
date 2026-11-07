namespace WowStopwatcher.App.Services;

/// <summary>
/// Platform-neutral shortcut modifiers. The values equal the Win32 MOD_* flags, which the Windows backend relies on.
/// <see cref="Win"/> is the Windows key, Command on macOS and Super on Linux.
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}
