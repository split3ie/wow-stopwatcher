using Avalonia;
using Avalonia.Controls;

namespace WowStopwatcher.App.Views;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Put keyboard focus into the dialog when it opens so Escape and Enter act on it.
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            CancelButton.Focus();
        }
    }
}
