using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WowStopwatcher.App.Controls;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>
/// The custom tray menu: a borderless topmost window next to the tray icon click position, on the side of the screen's working area where the taskbar is.
/// Rows are built in code from the engine snapshot and refreshed about every 250 ms while open.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private const double ShadowMargin = 32;
    private const double ScreenMargin = 12;

    private PixelPoint _anchor;
    private IStopwatchState _state = null!;
    private IStopwatchControl _control = null!;
    private Action _openMain = null!;
    private Action _quit = null!;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<Button> _items = [];
    private readonly List<Row> _rows = [];
    private TextBlock? _totalText;
    private string? _signature;
    private bool _activatedOnce;

    /// <summary>Required for the XAML runtime loader; call <see cref="Attach"/> before showing.</summary>
    public TrayMenuWindow()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Reposition();
            Opacity = 1;
            Activate();
            _timer.Start();
        };
        Activated += (_, _) => _activatedOnce = true;
        Deactivated += (_, _) =>
        {
            // Ignore the deactivation that can happen before the window was ever activated.
            if (_activatedOnce)
            {
                Close();
            }
        };
        Closed += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => Refresh();
    }

    public TrayMenuWindow Attach(IStopwatchState state, IStopwatchControl control, Action openMain, Action quit, PixelPoint anchor)
    {
        _anchor = anchor;
        _state = state;
        _control = control;
        _openMain = openMain;
        _quit = quit;
        Refresh();
        return this;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveFocus(int step)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var current = _items.IndexOf((FocusManager?.GetFocusedElement() as Button)!);
        var next = current < 0
            ? (step > 0 ? 0 : _items.Count - 1)
            : (current + step + _items.Count) % _items.Count;
        _items[next].Focus(NavigationMethod.Tab);
    }

    private void Reposition()
    {
        var screen = Screens.ScreenFromPoint(_anchor) ?? Screens.Primary ?? Screens.ScreenFromVisual(this);
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(Bounds.Width * scale);
        var height = (int)Math.Ceiling(Bounds.Height * scale);
        var pad = (int)Math.Round(ShadowMargin * scale);
        var margin = (int)Math.Round(ScreenMargin * scale);

        // Size of the visible card (the window minus its shadow margin).
        var cardWidth = width - 2 * pad;
        var cardHeight = height - 2 * pad;

        // The taskbar is on the side with the largest gap between the screen and its working area.
        var top = area.Y - bounds.Y;
        var bottom = bounds.Bottom - area.Bottom;
        var left = area.X - bounds.X;
        var right = bounds.Right - area.Right;
        var largest = Math.Max(Math.Max(top, bottom), Math.Max(left, right));

        int cardX;
        int cardY;
        if (largest <= 0)
        {
            // No visible taskbar (auto-hide): above the anchor if there is room, else below.
            cardX = _anchor.X - cardWidth / 2;
            cardY = _anchor.Y - margin - cardHeight;
            if (cardY < area.Y)
            {
                cardY = _anchor.Y + margin;
            }
        }
        else if (largest == bottom)
        {
            cardX = _anchor.X - cardWidth / 2;
            cardY = area.Bottom - margin - cardHeight;
        }
        else if (largest == top)
        {
            cardX = _anchor.X - cardWidth / 2;
            cardY = area.Y + margin;
        }
        else if (largest == left)
        {
            cardX = area.X + margin;
            cardY = _anchor.Y - cardHeight / 2;
        }
        else
        {
            cardX = area.Right - margin - cardWidth;
            cardY = _anchor.Y - cardHeight / 2;
        }

        // Keep the whole card inside the working area; only the shadow padding may extend past it.
        cardX = Math.Clamp(cardX, area.X, Math.Max(area.X, area.Right - cardWidth));
        cardY = Math.Clamp(cardY, area.Y, Math.Max(area.Y, area.Bottom - cardHeight));
        Position = new PixelPoint(cardX - pad, cardY - pad);
    }

    private void Refresh()
    {
        var snapshot = _state.GetSnapshot();
        var signature = string.Join('|', snapshot.Stopwatches.Select(s => $"{s.Id}:{s.Name}:{s.Color}:{s.IsIndependent}"));
        if (signature != _signature)
        {
            _signature = signature;
            Build(snapshot);
            if (IsVisible)
            {
                Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
            }
        }

        foreach (var row in _rows)
        {
            row.Update(snapshot);
        }

        if (_totalText is not null)
        {
            _totalText.Text = TimeFormat.Clock(snapshot.GrandTotal);
        }
    }

    private void Build(StopwatchEngineSnapshot snapshot)
    {
        Root.Children.Clear();
        _items.Clear();
        _rows.Clear();
        _totalText = null;

        if (snapshot.Stopwatches.Count == 0)
        {
            Root.Children.Add(MutedText(Loc.T("tray.empty"), new Thickness(10, 12)));
        }
        else
        {
            var header = MutedText(Loc.T("tray.header"), new Thickness(10, 8, 10, 6));
            header.FontSize = 11;
            header.FontWeight = FontWeight.SemiBold;
            header.LetterSpacing = 0.9;
            Root.Children.Add(header);

            foreach (var stopwatch in snapshot.Stopwatches)
            {
                var row = new Row(this, stopwatch);
                _rows.Add(row);
                AddItem(row.Button);
            }
        }

        Root.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(4, 6),
            Background = Brush("BorderBrush"),
        });

        if (snapshot.Stopwatches.Count > 0)
        {
            AddItem(ActionItem("M7,5 H17 A2,2 0 0 1 19,7 V17 A2,2 0 0 1 17,19 H7 A2,2 0 0 1 5,17 V7 A2,2 0 0 1 7,5 Z",
                true, Loc.T("tray.stopAll"), FontWeight.SemiBold, "TextBrush", () => _control.StopAll()));
        }

        AddItem(ActionItem("M5,4 H19 A2,2 0 0 1 21,6 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 V6 A2,2 0 0 1 5,4 Z M3,9 H21",
            false, Loc.T("tray.open"), FontWeight.Normal, "SecondaryBrush", _openMain));
        AddItem(ActionItem("M12,3 V12 M6.3,7 A8,8 0 1 0 17.7,7",
            false, Loc.T("tray.quit"), FontWeight.Normal, "SecondaryBrush", _quit));

        if (snapshot.Stopwatches.Count > 0)
        {
            _totalText = new TextBlock
            {
                FontFamily = (FontFamily)this.FindResource("MonoFont")!,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("TextBrush"),
            };
            var footer = new Border
            {
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(10, 6, 10, 4),
                BorderBrush = Brush("LineSoftBrush"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = Loc.T("tray.totalToday"), FontSize = 11, Foreground = Brush("MutedBrush") },
                        _totalText,
                    },
                },
            };
            _totalText.FontSize = 11;
            Root.Children.Add(footer);
        }
    }

    private void AddItem(Button button)
    {
        _items.Add(button);
        Root.Children.Add(button);
    }

    private Button ActionItem(
        string iconData, bool filled, string label, FontWeight weight, string foregroundKey, Action action)
    {
        var foreground = Brush(foregroundKey);
        var icon = new Icon
        {
            Width = 14,
            Height = 14,
            Data = Geometry.Parse(iconData),
            Fill = filled ? foreground : null,
            Stroke = filled ? null : foreground,
            StrokeThickness = 2,
        };
        var text = new TextBlock
        {
            Text = label,
            FontWeight = weight,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, text },
        };
        var button = new Button { Height = 40, Content = content };
        button.Classes.Add("trayItem");
        button.Click += (_, _) =>
        {
            Close();
            action();
        };
        return button;
    }

    private TextBlock MutedText(string text, Thickness margin) => new()
    {
        Text = text,
        Margin = margin,
        Foreground = Brush("MutedBrush"),
    };

    private IBrush Brush(string key) => (IBrush)this.FindResource(key)!;

    /// <summary>One stopwatch row: dot, name and today's total.</summary>
    private sealed class Row
    {
        private readonly TrayMenuWindow _owner;
        private readonly StopwatchDefinition _definition;
        private readonly Border _dot;
        private readonly TextBlock _name;
        private readonly TextBlock _time;
        private readonly Color _color;
        private bool? _active;

        public Row(TrayMenuWindow owner, StopwatchDefinition definition)
        {
            _owner = owner;
            _definition = definition;
            _color = Color.TryParse(definition.Color, out var color) ? color : Colors.Gray;

            _dot = new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(4.5),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _name = new TextBlock { Text = definition.Name, VerticalAlignment = VerticalAlignment.Center };
            _time = new TextBlock
            {
                FontFamily = (FontFamily)owner.FindResource("MonoFont")!,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid
            {
                Margin = new Thickness(10, 0),
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            };
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            var nameStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _name },
            };
            Grid.SetColumn(nameStack, 1);
            Grid.SetColumn(_time, 2);
            grid.Children.Add(_dot);
            grid.Children.Add(nameStack);
            grid.Children.Add(_time);

            Button = new Button { Height = 44, Content = grid };
            Button.Classes.Add("trayItem");
            Button.Click += (_, _) =>
            {
                owner.Close();
                owner._control.Toggle(definition.Id);
            };
        }

        public Button Button { get; }

        public void Update(StopwatchEngineSnapshot snapshot)
        {
            _time.Text = TimeFormat.Clock(snapshot.GetTodayTotal(_definition.Id));

            var active = snapshot.IsRunning(_definition.Id);
            var canToggle = active || snapshot.CanStart(_definition.Id);
            if (Button.IsEnabled != canToggle)
            {
                Button.IsEnabled = canToggle;
                ToolTip.SetTip(Button, canToggle ? null : Loc.Plural("tray.maxRunning", StopwatchEngine.MaxConcurrent));
            }

            if (_active == active)
            {
                return;
            }

            _active = active;
            Button.Classes.Set("active", active);
            _name.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
            _name.Foreground = _owner.Brush(active ? "TextBrush" : "SecondaryBrush");
            _time.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            _time.Foreground = _owner.Brush(active ? "AccentStrongBrush" : "MutedBrush");

            if (active)
            {
                _dot.Background = new SolidColorBrush(_color);
                _dot.BorderThickness = new Thickness(0);
                _dot.BoxShadow = new BoxShadows(new BoxShadow
                {
                    Spread = 3,
                    Color = Color.FromArgb(0x33, _color.R, _color.G, _color.B),
                });
            }
            else
            {
                _dot.Background = null;
                _dot.BorderBrush = new SolidColorBrush(_color);
                _dot.BorderThickness = new Thickness(2);
                _dot.BoxShadow = default;
            }
        }
    }
}
