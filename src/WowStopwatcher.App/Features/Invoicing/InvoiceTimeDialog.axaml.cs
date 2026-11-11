using Avalonia.Controls;
using Avalonia.Input;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Modal dialog with the time and notes of one stopwatch on one invoice day.</summary>
public partial class InvoiceTimeDialog : Window
{
    public InvoiceTimeDialog() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
