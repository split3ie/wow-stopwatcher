namespace WowStopwatcher.App.Services;

/// <summary>
/// Parses shortcut text written by the Settings page, for example "Ctrl+Alt+F1":
/// modifiers (Ctrl, Alt, Shift, Win) joined with "+", then exactly one key.
/// </summary>
public static class ShortcutParser
{
    private static readonly Dictionary<string, HotkeyKey> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = HotkeyKey.Space,
        ["PageUp"] = HotkeyKey.PageUp,
        ["PageDown"] = HotkeyKey.PageDown,
        ["End"] = HotkeyKey.End,
        ["Home"] = HotkeyKey.Home,
        ["Left"] = HotkeyKey.Left,
        ["Up"] = HotkeyKey.Up,
        ["Right"] = HotkeyKey.Right,
        ["Down"] = HotkeyKey.Down,
        ["Insert"] = HotkeyKey.Insert,
        ["Delete"] = HotkeyKey.Delete,
    };

    /// <summary>Returns false for null, empty or unparseable text.</summary>
    public static bool TryParse(string? text, out ShortcutKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToLowerInvariant() switch
            {
                "ctrl" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (flag == HotkeyModifiers.None || (modifiers & flag) != 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        if (!TryParseKey(parts[^1], out var parsed))
        {
            return false;
        }

        key = new ShortcutKey(modifiers, parsed);
        return true;
    }

    private static bool TryParseKey(string name, out HotkeyKey key)
    {
        key = HotkeyKey.None;
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = HotkeyKey.A + (c - 'A');
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = HotkeyKey.D0 + (c - '0');
                return true;
            }

            return false;
        }

        if (NamedKeys.TryGetValue(name, out key))
        {
            return true;
        }

        if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            key = HotkeyKey.F1 + (f - 1);
            return true;
        }

        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && name.Length == 7 && char.IsAsciiDigit(name[6]))
        {
            key = HotkeyKey.NumPad0 + (name[6] - '0');
            return true;
        }

        return false;
    }
}
