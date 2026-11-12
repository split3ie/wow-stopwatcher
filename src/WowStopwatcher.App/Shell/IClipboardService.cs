namespace WowStopwatcher.App.Shell;

/// <summary>Access to the system clipboard.</summary>
public interface IClipboardService
{
    /// <summary>Puts <paramref name="text"/> on the clipboard. Returns false when the clipboard is not available.</summary>
    Task<bool> SetTextAsync(string text);

    /// <summary>The clipboard text, or null when it is not text or reading the clipboard is not supported.</summary>
    Task<string?> TryGetTextAsync() => Task.FromResult<string?>(null);

    /// <summary>Empties the clipboard. Does nothing when that is not supported.</summary>
    Task ClearAsync() => Task.CompletedTask;
}
