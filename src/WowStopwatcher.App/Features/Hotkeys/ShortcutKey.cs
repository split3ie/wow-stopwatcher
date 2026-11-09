namespace WowStopwatcher.App.Features.Hotkeys;

/// <summary>A parsed shortcut: neutral modifier flags and a neutral key.</summary>
public readonly record struct ShortcutKey(HotkeyModifiers Modifiers, HotkeyKey Key);
