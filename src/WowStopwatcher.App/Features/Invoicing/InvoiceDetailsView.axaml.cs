using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WowStopwatcher.App.Features.Invoicing;

public partial class InvoiceDetailsView : UserControl
{
    public InvoiceDetailsView()
    {
        InitializeComponent();
        // A note is saved a moment after typing and at once when its box loses focus.
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox && DataContext is InvoiceDetailsViewModel viewModel)
        {
            viewModel.FlushNotes();
        }
    }
}
