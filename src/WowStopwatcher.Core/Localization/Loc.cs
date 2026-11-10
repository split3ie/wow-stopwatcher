using System.Globalization;

namespace WowStopwatcher.Core.Localization;

/// <summary>
/// Ambient access to the active language, used by all code and by the XAML <c>{loc:T key}</c> extension.
/// This is a deliberate exception to the constructor-injection convention: XAML markup extensions cannot use the DI
/// container, and the language is fixed for the life of the process (a change applies after a restart).
/// Defaults to English until <see cref="Use"/> is called at startup.
/// </summary>
public static class Loc
{
    private static IReadOnlyList<Translation>? _available;
    private static Translation? _current;

    /// <summary>Every available language, English first.</summary>
    public static IReadOnlyList<Translation> Available => _available ??= TranslationLoader.LoadAll();

    /// <summary>The active language; English until <see cref="Use"/> is called.</summary>
    public static Translation Current => _current ??= Available.FirstOrDefault(t => t.Code == LanguageResolver.DefaultCode)
        ?? new Translation(LanguageResolver.DefaultCode, "English", "English", new Dictionary<string, string>());

    /// <summary>Culture of the active language, for day and month names.</summary>
    public static CultureInfo Culture => Current.Culture;

    /// <summary>Sets the active language. Call once at startup, before any UI text is created.</summary>
    public static void Use(Translation translation) => _current = translation;

    /// <summary>The text for <paramref name="key"/>.</summary>
    public static string T(string key) => Current.Get(key);

    /// <summary>The text for <paramref name="key"/> with named arguments, e.g. <c>Loc.T("today.reset.title", ("name", "Focus"))</c>.</summary>
    public static string T(string key, params (string Name, object? Value)[] args) => Current.Get(key, args);

    /// <summary>The plural text for <paramref name="key"/> and <paramref name="count"/>; <c>{count}</c> is filled automatically.</summary>
    public static string Plural(string key, long count, params (string Name, object? Value)[] args) =>
        Current.GetPlural(key, count, args);
}
