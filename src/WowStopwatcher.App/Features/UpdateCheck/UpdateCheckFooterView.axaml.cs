using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WowStopwatcher.App.Features.UpdateCheck;

public partial class UpdateCheckFooterView : UserControl
{
    public UpdateCheckFooterView() => InitializeComponent();

    /// <summary>Opens the release page (https only) in the browser.</summary>
    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string url }
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
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
