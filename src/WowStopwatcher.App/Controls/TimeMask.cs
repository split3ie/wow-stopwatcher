using Avalonia.Input;

namespace WowStopwatcher.App.Controls;

/// <summary>
/// Overtype editing of a fixed-shape time text such as "000:00" or "00:00". In the shape every '0' is a digit
/// and any other character is fixed (skipped by the caret). Minute tens stay at 5 or less; the hours stay
/// at <c>maxHours</c> or less (for example 23 for a clock time).
/// </summary>
public static class TimeMask
{
    /// <summary>True when <paramref name="text"/> has the shape and its digits are valid.</summary>
    public static bool Matches(string shape, string? text, int maxHours = int.MaxValue) =>
        text is not null && text.Length == shape.Length && IsValid(shape, text, maxHours);

    /// <summary>
    /// Applies a digit, Backspace or Delete key to <paramref name="text"/>. Text that does not match the shape starts
    /// from all zeros. Returns false when the key changes nothing; otherwise the new text and caret position.
    /// </summary>
    public static bool TryApply(string shape, string? text, int caret, Key key, out string result, out int newCaret, int maxHours = int.MaxValue)
    {
        result = text ?? string.Empty;
        newCaret = caret;
        var chars = (Matches(shape, text, maxHours) ? text! : Zeros(shape)).ToCharArray();
        var pos = Math.Clamp(caret, 0, chars.Length);

        if (DigitOf(key) is { } digit)
        {
            pos = SkipFixedForward(shape, pos);
            if (pos >= chars.Length)
            {
                return false;
            }

            chars[pos] = (char)('0' + digit);
            pos = SkipFixedForward(shape, pos + 1);
            if (!IsValid(shape, new string(chars), maxHours))
            {
                return false;
            }
        }
        else if (key == Key.Back)
        {
            do
            {
                pos--;
            }
            while (pos >= 0 && IsFixed(shape, pos));

            if (pos < 0)
            {
                return false;
            }

            chars[pos] = '0';
        }
        else if (key == Key.Delete)
        {
            pos = SkipFixedForward(shape, pos);
            if (pos >= chars.Length)
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

    private static string Zeros(string shape) => shape;

    private static bool IsFixed(string shape, int index) => shape[index] != '0';

    private static int SkipFixedForward(string shape, int pos)
    {
        while (pos < shape.Length && IsFixed(shape, pos))
        {
            pos++;
        }

        return pos;
    }

    private static bool IsValid(string shape, string text, int maxHours)
    {
        for (var i = 0; i < shape.Length; i++)
        {
            if (IsFixed(shape, i) ? text[i] != shape[i] : !char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        var colon = shape.IndexOf(':');
        if (colon < 0)
        {
            return true;
        }

        if (text[colon + 1] > '5')
        {
            return false;
        }

        return int.Parse(text.AsSpan(0, colon), provider: System.Globalization.CultureInfo.InvariantCulture) <= maxHours;
    }

    private static int? DigitOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };
}
