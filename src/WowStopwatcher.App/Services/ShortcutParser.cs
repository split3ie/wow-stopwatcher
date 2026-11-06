namespace WowStopwatcher.App.Services;

/// <summary>
/// Parses shortcut text written by the Settings page, for example "Ctrl+Alt+F1":
/// modifiers (Ctrl, Alt, Shift, Win) joined with "+", then exactly one key.
/// </summary>
public static class ShortcutParser
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["End"] = 0x23,
        ["Home"] = 0x24,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
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
        uint modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToLowerInvariant() switch
            {
                "ctrl" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" => ModWin,
                _ => 0u,
            };
            if (flag == 0 || (modifiers & flag) != 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        if (!TryParseKey(parts[^1], out var vk))
        {
            return false;
        }

        key = new ShortcutKey(modifiers, vk);
        return true;
    }

    private static bool TryParseKey(string name, out uint vk)
    {
        vk = 0;
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c; // VK codes for A-Z and 0-9 equal their ASCII codes.
                return true;
            }

            return false;
        }

        if (NamedKeys.TryGetValue(name, out vk))
        {
            return true;
        }

        if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        if (name.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && name.Length == 7 && char.IsAsciiDigit(name[6]))
        {
            vk = (uint)(0x60 + (name[6] - '0'));
            return true;
        }

        return false;
    }
}
