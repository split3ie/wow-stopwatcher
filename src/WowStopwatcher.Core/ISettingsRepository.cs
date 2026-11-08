namespace WowStopwatcher.Core;

/// <summary>Persistent storage of app settings as string key/value pairs.</summary>
public interface ISettingsRepository
{
    /// <summary>The value stored for <paramref name="key"/>, or null when the key is not set.</summary>
    string? Get(string key);

    /// <summary>Stores <paramref name="value"/> for <paramref name="key"/>, replacing any existing value.</summary>
    void Set(string key, string value);
}
