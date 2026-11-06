using Avalonia.Input;

namespace WowStopwatcher.App.ViewModels;

/// <summary>Overtype editing of a "hh:mm:ss" text: digits replace the character at the caret and colons are skipped.</summary>
internal static class SettingsGoalMask
{
    public const string Empty = "00:00:00";

    /// <summary>
    /// Applies a digit, Backspace or Delete key to <paramref name="text"/> (8 characters). Returns false when the
    /// key changes nothing; otherwise the new text and caret position.
    /// </summary>
    public static bool TryApply(string text, int caret, Key key, out string result, out int newCaret)
    {
        result = text;
        newCaret = caret;
        if (text.Length != Empty.Length)
        {
            text = Empty;
        }

        var chars = text.ToCharArray();
        var pos = Math.Clamp(caret, 0, chars.Length);

        if (DigitOf(key) is { } digit)
        {
            if (pos is 2 or 5)
            {
                pos++;
            }

            if (pos > 7 || (pos is 3 or 6 && digit > 5))
            {
                return false;
            }

            chars[pos] = (char)('0' + digit);
            pos++;
            if (pos is 2 or 5)
            {
                pos++;
            }
        }
        else if (key == Key.Back)
        {
            if (pos == 0)
            {
                return false;
            }

            pos--;
            if (pos is 2 or 5)
            {
                pos--;
            }

            chars[pos] = '0';
        }
        else if (key == Key.Delete)
        {
            if (pos is 2 or 5)
            {
                pos++;
            }

            if (pos > 7)
            {
                return false;
            }

            chars[pos] = '0';
        }
        else
        {
            return false;
        }

        result = new string(chars);
        newCaret = pos;
        return true;
    }

    private static int? DigitOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };
}
