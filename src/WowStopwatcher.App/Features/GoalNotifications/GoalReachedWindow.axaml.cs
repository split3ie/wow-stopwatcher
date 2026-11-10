using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.GoalNotifications;

/// <summary>
/// A small non-activating popup in the corner of the primary screen's working area (the side of the taskbar) saying that
/// goals were reached. Closes after a few seconds (paused while the pointer is over it); clicking it opens the main window.
/// </summary>
public partial class GoalReachedWindow : Window
{
    private const double ShadowMargin = 32;
    private const double ScreenMargin = 12;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    private readonly DispatcherTimer _timer = new() { Interval = Lifetime };
    private Action _openMain = () => { };

    /// <summary>Required for the XAML runtime loader.</summary>
    public GoalReachedWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Reposition();
            Opacity = 1;
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Close();
        PointerEntered += (_, _) => _timer.Stop();
        PointerExited += (_, _) => RestartCountdown();
        Card.PointerPressed += OnCardPressed;
    }

    public GoalReachedWindow Attach(Action openMain)
    {
        _openMain = openMain;
        return this;
    }

    /// <summary>Adds a line for the stopwatch and restarts the countdown.</summary>
    public void AddGoal(StopwatchDefinition definition)
    {
        var color = Color.TryParse(definition.Color, out var parsed) ? parsed : Colors.Gray;
        var text = new TextBlock
        {
            Text = Loc.T("goalNotify.line", ("name", definition.Name), ("goal", TimeFormat.GoalLabel(definition.Goal ?? TimeSpan.Zero))),
            Foreground = (IBrush)this.FindResource("SecondaryBrush")!,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var dot = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(4.5),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Lines.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { dot, text },
        });

        if (IsVisible)
        {
            Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
        }

        RestartCountdown();
    }

    private void RestartCountdown()
    {
        _timer.Stop();
        if (!IsPointerOver)
        {
            _timer.Start();
        }
    }

    private void OnCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || (e.Source as Visual)?.FindAncestorOfType<Button>(true) is not null)
        {
            return;
        }

        e.Handled = true;
        Close();
        _openMain();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void Reposition()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromVisual(this);
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        var pad = (int)Math.Round(ShadowMargin * scale);
        var margin = (int)Math.Round(ScreenMargin * scale);
        var cardWidth = (int)Math.Ceiling(Bounds.Width * scale) - 2 * pad;
        var cardHeight = (int)Math.Ceiling(Bounds.Height * scale) - 2 * pad;

        // The taskbar is on the side with the largest gap between the screen and its working area.
        // Top only when that is clearly the taskbar side; otherwise (bottom, left, right, hidden) use the bottom.
        var topGap = area.Y - bounds.Y;
        var otherGap = Math.Max(Math.Max(bounds.Bottom - area.Bottom, area.X - bounds.X), bounds.Right - area.Right);
        var atTop = topGap > 0 && topGap > otherGap;

        var cardX = area.Right - margin - cardWidth;
        var cardY = atTop ? area.Y + margin : area.Bottom - margin - cardHeight;
        Position = new PixelPoint(cardX - pad, cardY - pad);
    }
}
