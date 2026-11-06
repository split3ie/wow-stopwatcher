namespace WowStopwatcher.App.Services;

/// <summary>A parsed shortcut: Win32 modifier flags (MOD_*) and a virtual-key code.</summary>
public readonly record struct ShortcutKey(uint Modifiers, uint VirtualKey);
