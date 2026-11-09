using Avalonia;
using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private bool _sized;

    /// <summary>True when the close button only hides the window (the app keeps running); false when it quits the app.</summary>
    public bool CloseHidesWindow { get; init; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    /// <summary>Once per window: shrinks the window to the working area of its screen when 1200x880 does not fit, then centers it.</summary>
    private void FitToScreen()
    {
        if (_sized)
        {
            return;
        }

        _sized = true;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null || screen.Scaling <= 0)
        {
            return;
        }

        const double margin = 40; // DIPs kept free around the window
        var scaling = screen.Scaling;
        var area = screen.WorkingArea;
        var maxWidth = Math.Max(1, area.Width / scaling - margin);
        var maxHeight = Math.Max(1, area.Height / scaling - margin);

        var width = Math.Min(Width, maxWidth);
        var height = Math.Min(Height, maxHeight);
        MinWidth = Math.Min(MinWidth, width);
        MinHeight = Math.Min(MinHeight, height);
        Width = width;
        Height = height;

        // Center on the screen's working area (position is in physical pixels).
        var x = area.X + (int)Math.Round((area.Width - width * scaling) / 2);
        var y = area.Y + (int)Math.Round((area.Height - height * scaling) / 2);
        Position = new PixelPoint(x, y);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // With CloseHidesWindow the close button hides to the tray; app shutdown and OS shutdown still close the window.
        if (CloseHidesWindow && e.CloseReason == WindowCloseReason.WindowClosing)
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
