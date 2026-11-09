using WowStopwatcher.App.Composition;

namespace WowStopwatcher.App.Features.UpdateCheck;

/// <summary>Deletes the old exe or bundle that a previous update renamed to ".old".</summary>
public sealed class UpdateLeftoverCleaner : IAppService
{
    public void Start()
    {
        var path = UpdateInstaller.OldLeftoverPath();
        if (path is null)
        {
            return;
        }

        // The old process may still be exiting, so retry a few times in the background.
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    else if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
            }
        });
    }

    public void Dispose()
    {
    }
}
