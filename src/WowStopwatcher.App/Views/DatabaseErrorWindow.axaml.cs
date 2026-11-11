using Avalonia.Controls;
using Avalonia.Interactivity;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Views;

public partial class DatabaseErrorWindow : Window
{
    public DatabaseErrorWindow() => InitializeComponent();

    public DatabaseErrorWindow(DatabaseErrorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Title = viewModel.Title;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

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
