namespace WowStopwatcher.App.Shell;

/// <summary>Access to the system clipboard.</summary>
public interface IClipboardService
{
    /// <summary>Puts <paramref name="text"/> on the clipboard. Returns false when the clipboard is not available.</summary>
    Task<bool> SetTextAsync(string text);
}
