using Avalonia.Input;

namespace WowStopwatcher.App.ViewModels;

/// <summary>
/// Maps Avalonia keys to the stored shortcut text. Format: modifiers in the order Ctrl, Alt, Shift, Win joined with
/// "+", then one key (A-Z, 0-9, F1-F24, Space, arrows, Home, End, PageUp, PageDown, Insert, Delete, NumPad0-NumPad9).
/// </summary>
internal static class SettingsShortcutKeys
{
    /// <summary>How the stored "Win" modifier is named on this OS: Cmd on macOS, Super on Linux, Win on Windows.</summary>
    private static readonly string WinLabel = OperatingSystem.IsMacOS() ? "Cmd" : OperatingSystem.IsLinux() ? "Super" : "Win";

    /// <summary>Shortcut text for display: the stored "Win" modifier is shown with the OS name. Stored text is not changed.</summary>
    public static string ToDisplay(string text)
    {
        if (WinLabel == "Win" || !text.Contains("Win", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        var parts = text.Split('+');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i].Trim(), "Win", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = WinLabel;
            }
        }

        return string.Join('+', parts);
    }

    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    /// <summary>"Ctrl+Alt" style prefix of the held modifiers (without a trailing "+").</summary>
    public static IReadOnlyList<string> Modifiers(KeyModifiers modifiers)
    {
        var list = new List<string>(4);
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            list.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            list.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            list.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            list.Add("Win");
        }

        return list;
    }

    /// <summary>The stored name of a supported key, or null when the key cannot be used in a shortcut.</summary>
    public static string? KeyName(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return "F" + (key - Key.F1 + 1);
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "NumPad" + (key - Key.NumPad0);
        }

        return key switch
        {
            Key.Space => "Space",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            _ => null,
        };
    }

    /// <summary>True for F1-F12, which are allowed without a modifier.</summary>
    public static bool IsFunctionKey(string name) =>
        name.Length is 2 or 3
        && name[0] == 'F'
        && int.TryParse(name.AsSpan(1), out var n)
        && n is >= 1 and <= 12;
}
