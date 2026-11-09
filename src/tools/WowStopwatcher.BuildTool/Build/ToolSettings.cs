namespace WowStopwatcher.BuildTool.Build;

/// <summary>Persisted tool settings; only the output folder is stored.</summary>
public sealed class ToolSettings
{
    public string? OutputFolder { get; set; }
}
