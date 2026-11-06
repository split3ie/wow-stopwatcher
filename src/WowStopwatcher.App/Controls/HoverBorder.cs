using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace WowStopwatcher.App.Controls;

/// <summary>
/// A border that reports to a view model (two-way <see cref="IsActive"/>) while the pointer is over it
/// or keyboard focus is inside it. Used to drive the custom timeline tooltips.
/// </summary>
public class HoverBorder : Border
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<HoverBorder, bool>(nameof(IsActive), defaultBindingMode: BindingMode.TwoWay);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private bool _pointerOver;

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerOver = true;
        UpdateActive();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = false;
        UpdateActive();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsKeyboardFocusWithinProperty)
        {
            UpdateActive();
        }
    }

    private void UpdateActive() => IsActive = _pointerOver || IsKeyboardFocusWithin;
}
