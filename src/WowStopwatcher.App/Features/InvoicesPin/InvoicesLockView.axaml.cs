using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WowStopwatcher.App.Features.InvoicesPin;

public partial class InvoicesLockView : UserControl
{
    public InvoicesLockView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(() => PinBox.Focus());
        }
    }
}
