namespace WowStopwatcher.App.Shell;

/// <summary><see cref="IStartupLanguage"/> over <see cref="StartupLanguage"/>. Remembers whether a language was set, so an error window can keep it.</summary>
public sealed class StartupLanguageApplier : IStartupLanguage
{
    public bool HasApplied { get; private set; }

    public void Apply(string? storedLanguage)
    {
        StartupLanguage.Apply(storedLanguage);
        HasApplied = true;
    }
}
