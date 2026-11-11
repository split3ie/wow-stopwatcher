using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WowStopwatcher.App.Controls;

/// <summary>
/// Attached behavior that limits a <see cref="TextBox"/> to a fixed-shape time text (see <see cref="TimeMask"/>):
/// digits overtype, Backspace and Delete set a digit to 0, and letters, paste, cut and undo are blocked.
/// Usage: <c>c:TimeMaskBehavior.Shape="000:00"</c>.
/// </summary>
public static class TimeMaskBehavior
{
    public static readonly AttachedProperty<string?> ShapeProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, string?>("Shape");

    /// <summary>The largest hours value allowed (for example 23 for a clock time); 0 or less means no limit.</summary>
    public static readonly AttachedProperty<int> MaxHoursProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, int>("MaxHours");

    static TimeMaskBehavior() => ShapeProperty.Changed.AddClassHandler<TextBox>(OnShapeChanged);

    public static int GetMaxHours(TextBox box) => box.GetValue(MaxHoursProperty);

    public static void SetMaxHours(TextBox box, int value) => box.SetValue(MaxHoursProperty, value);

    public static string? GetShape(TextBox box) => box.GetValue(ShapeProperty);

    public static void SetShape(TextBox box, string? value) => box.SetValue(ShapeProperty, value);

    private static void OnShapeChanged(TextBox box, AvaloniaPropertyChangedEventArgs e)
    {
        box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        box.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        if (e.NewValue is string { Length: > 0 })
        {
            box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            box.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
            box.ContextFlyout = null;
        }
    }

    private static void OnTextInput(object? sender, TextInputEventArgs e) => e.Handled = true;

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || GetShape(box) is not { Length: > 0 } shape)
        {
            return;
        }

        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Home or Key.End
            || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Copy and select-all stay; paste, cut and undo would break the fixed shape.
            if (e.Key is not (Key.C or Key.A))
            {
                e.Handled = true;
            }

            return;
        }

        var isDigit = e.Key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!isDigit && e.Key is not (Key.Back or Key.Delete or Key.Insert))
        {
            return; // Enter, Escape and the like keep working; typed characters are blocked as text input.
        }

        e.Handled = true;
        if (shift || e.Key == Key.Insert)
        {
            return;
        }

        if (TimeMask.TryApply(shape, box.Text, box.CaretIndex, e.Key, out var text, out var caret, GetMaxHours(box) is > 0 and var max ? max : int.MaxValue))
        {
            box.Text = text;
            box.SelectionStart = caret;
            box.SelectionEnd = caret;
            box.CaretIndex = caret;
        }
    }
}
