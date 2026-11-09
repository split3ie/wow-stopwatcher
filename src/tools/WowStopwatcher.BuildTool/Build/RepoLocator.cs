namespace WowStopwatcher.BuildTool.Build;

/// <summary>Finds the repository root by walking up to the folder that contains <c>src/WowStopwatcher.slnx</c>.</summary>
public static class RepoLocator
{
    /// <summary>Returns the repository root, or null when not found.</summary>
    public static string? FindRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "WowStopwatcher.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
