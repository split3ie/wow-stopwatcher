using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WowStopwatcher.App.Features.Invoicing;

public partial class InvoicingView : UserControl
{
    public InvoicingView()
    {
        InitializeComponent();

        // Any input while the page is shown restarts the PIN idle timer.
        AddHandler(PointerMovedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnInput(object? sender, RoutedEventArgs e) => (DataContext as InvoicingViewModel)?.RecordActivity();
}
