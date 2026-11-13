using Avalonia.Controls;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class RecoveryPromptWindow : Window
{
    public RecoveryPromptWindow() => InitializeComponent();

    public RecoveryPromptWindow(RecoveryPromptViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += (_, _) => CodeBox.Focus();
    }
}
