using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Windowing;
using WowStopwatcher.BuildTool.ViewModels;

namespace WowStopwatcher.BuildTool.Views;

/// <summary>Main window: navigation, options, build controls and live log.</summary>
public partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty)
        {
            UpdateBackground();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateBackground();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Log))
                {
                    LogBox.CaretIndex = int.MaxValue;
                }
            };
        }
    }

    /// <summary>Uses a solid theme background when Mica is not available (for example outside Windows 11).</summary>
    private void UpdateBackground()
    {
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica)
        {
            Background = Brushes.Transparent;
        }
        else if (this.TryFindResource("SolidBackgroundFillColorBaseBrush", ActualThemeVariant, out var brush)
            && brush is IBrush solid)
        {
            Background = solid;
        }
    }

    private async void OnCopyLogClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && Clipboard is not null)
        {
            await Clipboard.SetTextAsync(vm.Log);
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Output folder",
            AllowMultiple = false,
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
        {
            vm.OutputFolder = path;
        }
    }
}
