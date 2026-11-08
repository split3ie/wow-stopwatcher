namespace WowStopwatcher.App.Services;

/// <summary>
/// Platform-neutral key of a shortcut. Each hotkey backend maps it to its own codes. Members of a group
/// (letters, digits, F-keys, keypad digits) are contiguous so backends can map them by offset.
/// </summary>
public enum HotkeyKey
{
    None = 0,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Space, PageUp, PageDown, End, Home, Left, Up, Right, Down, Insert, Delete,
}
