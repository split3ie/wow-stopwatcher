using System.Text.Json;

namespace WowStopwatcher.BuildTool.Build;

/// <summary>
/// Reads and writes <see cref="ToolSettings"/> as JSON in its own folder under ApplicationData
/// (<c>WowStopwatcher.BuildTool</c>, never the app's <c>WowStopwatcher</c> data folder). Errors never throw.
/// </summary>
public static class ToolSettingsStore
{
    /// <summary>Full path of settings.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WowStopwatcher.BuildTool",
        "settings.json");

    /// <summary>Loads the settings; returns empty settings when the file is missing, unreadable or invalid.</summary>
    public static ToolSettings Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ToolSettings>(File.ReadAllText(path)) ?? new ToolSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException)
        {
            return new ToolSettings();
        }
    }

    /// <summary>Saves the settings; returns false when the file cannot be written.</summary>
    public static bool Save(string path, ToolSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
