namespace WowStopwatcher.BuildTool.ViewModels;

/// <summary>Builds the result line shown in the bottom bar after a build ends.</summary>
public static class BuildSummary
{
    /// <summary>Summarizes the targets that ran; empty status means the target never started.</summary>
    public static string Create(IEnumerable<TargetOptionViewModel> targets, bool cancelled, DateTime finishedLocal)
    {
        var list = targets.ToList();
        var ok = Names(list, "ok");
        var failed = Names(list, "failed");
        var time = finishedLocal.ToString("HH:mm");

        if (cancelled)
        {
            return $"Build cancelled at {time}." + Part("Succeeded", ok) + Part("Failed", failed);
        }

        if (failed.Count > 0)
        {
            return $"Build failed at {time}: {string.Join(", ", failed)}." + Part("Succeeded", ok);
        }

        return $"Build succeeded at {time}: {string.Join(", ", ok)}.";
    }

    private static List<string> Names(List<TargetOptionViewModel> targets, string status) =>
        targets.Where(t => t.Status == status).Select(t => t.Label).ToList();

    private static string Part(string title, List<string> names) =>
        names.Count == 0 ? "" : $" {title}: {string.Join(", ", names)}.";
}
