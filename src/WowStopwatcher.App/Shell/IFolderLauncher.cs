namespace WowStopwatcher.App.Shell;

/// <summary>Opens a folder in the system file manager.</summary>
public interface IFolderLauncher
{
    /// <summary>Opens <paramref name="path"/>; returns false when it does not exist or the system cannot open it.</summary>
    Task<bool> OpenAsync(string path);
}
