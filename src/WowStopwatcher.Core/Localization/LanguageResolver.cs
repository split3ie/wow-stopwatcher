using System.Globalization;

namespace WowStopwatcher.Core.Localization;

/// <summary>Chooses the language to run in from the stored setting and the system language.</summary>
public static class LanguageResolver
{
    /// <summary>Key of the language setting in <see cref="ISettingsRepository"/>.</summary>
    public const string SettingKey = "language";

    /// <summary>Setting value (also used when the setting is missing) that follows the system language.</summary>
    public const string SystemValue = "system";

    /// <summary>The fallback language, also the one every other file falls back to.</summary>
    public const string DefaultCode = "en";

    /// <summary>
    /// The stored code when it is available; otherwise the system language when it is available; otherwise English.
    /// Codes are compared ignoring case; the result uses the spelling from <paramref name="availableCodes"/>.
    /// </summary>
    public static string Resolve(IEnumerable<string> availableCodes, string? stored, CultureInfo systemCulture)
    {
        var available = availableCodes.ToList();

        if (!string.IsNullOrWhiteSpace(stored) && !string.Equals(stored, SystemValue, StringComparison.OrdinalIgnoreCase))
        {
            var match = Find(available, stored.Trim());
            if (match is not null)
            {
                return match;
            }
        }

        return Find(available, systemCulture.TwoLetterISOLanguageName) ?? DefaultCode;
    }

    private static string? Find(List<string> available, string code) =>
        available.Find(a => string.Equals(a, code, StringComparison.OrdinalIgnoreCase));
}
