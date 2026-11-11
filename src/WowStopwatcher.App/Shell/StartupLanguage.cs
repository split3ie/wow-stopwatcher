using System.Globalization;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Shell;

/// <summary>Picks the language from the stored <c>language</c> setting and the system language, and activates it.</summary>
public static class StartupLanguage
{
    /// <summary>Call once at startup, before any window or view model is created.</summary>
    public static void Apply(ISettingsRepository settings) => Apply(settings.Get(LanguageResolver.SettingKey));

    /// <summary>Same, from the stored value (<c>null</c> follows the system language).</summary>
    public static void Apply(string? stored)
    {
        var code = LanguageResolver.Resolve(
            Loc.Available.Select(t => t.Code),
            stored,
            CultureInfo.CurrentUICulture);

        var translation = Loc.Available.FirstOrDefault(t => t.Code == code);
        if (translation is not null)
        {
            Loc.Use(translation);
        }
    }
}
