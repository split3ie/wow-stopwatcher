using Avalonia;
using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The close button hides to the tray; app shutdown and OS shutdown still close the window.
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty || change.Property == IsVisibleProperty)
        {
            // Refresh fast only while the window can actually be seen.
            var visible = IsVisible && WindowState != WindowState.Minimized;
            (DataContext as MainViewModel)?.SetWindowVisible(visible);
        }
    }
}
