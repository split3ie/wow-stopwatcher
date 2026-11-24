using System.Globalization;

namespace WowStopwatcher.Core;

/// <summary>Keeps the last-alive time in the app settings (<c>last_alive_utc</c>, round-trip format).</summary>
public sealed class SettingsLastAliveStore(ISettingsRepository settings) : ILastAliveStore
{
    public const string Key = "last_alive_utc";

    public DateTimeOffset? Read() =>
        DateTimeOffset.TryParse(settings.Get(Key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value.ToUniversalTime()
            : null;

    public void Write(DateTimeOffset utc) => settings.Set(Key, utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
}
