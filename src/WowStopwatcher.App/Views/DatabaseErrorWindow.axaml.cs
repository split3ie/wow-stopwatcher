using Avalonia.Controls;
using Avalonia.Interactivity;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class DatabaseErrorWindow : Window
{
    private readonly DatabaseRecovery? _recovery;

    public DatabaseErrorWindow() => InitializeComponent();

    public DatabaseErrorWindow(DatabaseErrorViewModel viewModel, DatabaseRecovery recovery) : this()
    {
        DataContext = viewModel;
        Title = viewModel.Title;
        _recovery = recovery;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnRetryClick(object? sender, RoutedEventArgs e) => _recovery?.TryAgain();

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_recovery is not null && DataContext is DatabaseErrorViewModel viewModel)
        {
            viewModel.ActionError = await _recovery.ChooseFolderAsync();
        }
    }

    private void OnUseDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (_recovery is not null && DataContext is DatabaseErrorViewModel viewModel)
        {
            viewModel.ActionError = _recovery.UseDefaultLocation();
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DatabaseErrorViewModel { CanDownload: true } viewModel
            || !Uri.TryCreate(viewModel.ReleasesUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        try
        {
            await launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // No browser available; nothing else to do.
        }
    }
}
