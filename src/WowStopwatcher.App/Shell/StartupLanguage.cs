using System.Globalization;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Shell;

/// <summary>Picks the language from the stored <c>language</c> setting and the system language, and activates it.</summary>
public static class StartupLanguage
{
    /// <summary>Call once at startup, before any window or view model is created.</summary>
    public static void Apply(ISettingsRepository settings)
    {
        var code = LanguageResolver.Resolve(
            Loc.Available.Select(t => t.Code),
            settings.Get(LanguageResolver.SettingKey),
            CultureInfo.CurrentUICulture);

        var translation = Loc.Available.FirstOrDefault(t => t.Code == code);
        if (translation is not null)
        {
            Loc.Use(translation);
        }
    }
}
