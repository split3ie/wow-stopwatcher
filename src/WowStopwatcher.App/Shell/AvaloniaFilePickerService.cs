using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IFilePickerService"/> over the storage provider of the application's main window.</summary>
public sealed class AvaloniaFilePickerService(IClassicDesktopStyleApplicationLifetime desktop) : IFilePickerService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (GetStorage() is not { CanPickFolder: true } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title, string filterName, IReadOnlyList<string> extensions)
    {
        if (GetStorage() is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [CreateFileType(filterName, extensions)],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
    {
        if (GetStorage() is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [CreateFileType(extension.TrimStart('.'), [extension])],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType CreateFileType(string name, IReadOnlyList<string> extensions) =>
        new(name) { Patterns = [.. extensions.Select(e => "*." + e.TrimStart('.'))] };

    private IStorageProvider? GetStorage() => TopLevel.GetTopLevel(desktop.MainWindow)?.StorageProvider;
}
