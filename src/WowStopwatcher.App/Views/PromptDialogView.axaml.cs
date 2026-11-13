using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WowStopwatcher.App.Views;

public partial class PromptDialogView : UserControl
{
    public PromptDialogView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(() => FirstBox.Focus());
        }
    }
}
