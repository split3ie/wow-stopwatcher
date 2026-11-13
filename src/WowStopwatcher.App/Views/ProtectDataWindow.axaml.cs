using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class ProtectDataWindow : Window
{
    public ProtectDataWindow() => InitializeComponent();

    public ProtectDataWindow(ProtectDataViewModel viewModel) : this() => DataContext = viewModel;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The data is being encrypted: closing now would leave the user without a result.
        if (DataContext is ProtectDataViewModel { IsWorking: true })
        {
            e.Cancel = true;
        }
        else if (DataContext is ProtectDataViewModel viewModel)
        {
            _ = viewModel.ClearCopiedCodeAsync();
        }

        base.OnClosing(e);
    }
}
