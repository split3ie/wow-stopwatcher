using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class SettingsStopwatchView : UserControl
{
    public SettingsStopwatchView()
    {
        InitializeComponent();
        Focusable = true;
        IsTabStop = false;

        // The goal box is edited by overtyping, so keys are intercepted before the TextBox sees them.
        GoalBox.AddHandler(KeyDownEvent, OnGoalKeyDown, RoutingStrategies.Tunnel);
        GoalBox.AddHandler(TextInputEvent, OnGoalTextInput, RoutingStrategies.Tunnel);

        NameBox.Loaded += OnNameBoxLoaded;
        NameBox.LostFocus += (_, _) => Vm?.CommitName();

        SwatchList.AddHandler(Button.ClickEvent, OnSwatchClick);
        if (ColorButton.Flyout is { } colorFlyout)
        {
            colorFlyout.Closed += (_, _) => Vm?.CancelCustomColor();
        }
        DataContextChanged += OnDataContextChanged;

        Recorder.GotFocus += (_, _) => Vm?.BeginRecording();
        Recorder.LostFocus += (_, _) => Vm?.EndRecording();
        Recorder.PointerPressed += OnRecorderPointerPressed;
        Recorder.KeyDown += OnRecorderKeyDown;
        Recorder.KeyUp += (_, e) => Vm?.HandleKeyUp(e.KeyModifiers);
    }

    private SettingsStopwatchViewModel? Vm => DataContext as SettingsStopwatchViewModel;

    // Click is raised before the button runs its Command. Hiding the flyout right away detaches the swatch
    // and its binding, so PickCommand never ran. Hide after the command has executed.
    private void OnSwatchClick(object? sender, RoutedEventArgs e) => HideColorFlyoutLater();

    private void HideColorFlyoutLater() => Dispatcher.UIThread.Post(() => ColorButton.Flyout?.Hide());

    private SettingsStopwatchViewModel? _subscribed;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.ColorFlyoutCloseRequested -= HideColorFlyoutLater;
        }

        _subscribed = Vm;
        if (_subscribed is not null)
        {
            _subscribed.ColorFlyoutCloseRequested += HideColorFlyoutLater;
        }
    }

    private void OnNameBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { FocusNameRequested: true } vm)
        {
            return;
        }

        vm.FocusNameRequested = false;
        Dispatcher.UIThread.Post(() =>
        {
            this.BringIntoView();
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }

    private void OnRecorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Recorder.Focus();
        Vm?.BeginRecording();
        e.Handled = true;
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || !vm.HandleKeyDown(e.Key, e.KeyModifiers, out var finished))
        {
            return;
        }

        e.Handled = true;
        if (finished)
        {
            Focus(); // park focus on the card so the recorder stops listening
        }
    }

    private void OnGoalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Home or Key.End
            || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Copy and select-all stay; paste, cut and undo would break the fixed hh:mm:ss shape.
            if (e.Key is Key.C or Key.A)
            {
                return;
            }

            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (Vm is not { } vm || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (SettingsGoalMask.TryApply(vm.GoalText, GoalBox.CaretIndex, e.Key, out var text, out var caret))
        {
            vm.GoalText = text;
            GoalBox.Text = text;
            GoalBox.SelectionStart = caret;
            GoalBox.SelectionEnd = caret;
            GoalBox.CaretIndex = caret;
        }
    }

    private static void OnGoalTextInput(object? sender, TextInputEventArgs e) => e.Handled = true;
}
