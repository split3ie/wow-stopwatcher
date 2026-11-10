using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Settings;

/// <summary>The language card in the General section of Settings. A change is stored at once and applies after a restart.</summary>
public sealed partial class LanguageSettingsViewModel : ObservableObject, ISettingsGeneralItem
{
    private readonly ISettingsRepository _settings;
    private readonly IAppLifetime _lifetime;

    public LanguageSettingsViewModel(ISettingsRepository settings, IAppLifetime lifetime)
    {
        _settings = settings;
        _lifetime = lifetime;

        var options = new List<LanguageOption> { new(LanguageResolver.SystemValue, Loc.T("settings.language.system")) };
        options.AddRange(Loc.Available.Select(t => new LanguageOption(t.Code, t.NativeName)));
        Options = options;

        var stored = settings.Get(LanguageResolver.SettingKey) ?? LanguageResolver.SystemValue;
        _selected = options.FirstOrDefault(o => string.Equals(o.Value, stored, StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }

    public int Order => -10;

    public IReadOnlyList<LanguageOption> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsRestart), nameof(ShowRestartButton))]
    private LanguageOption _selected;

    /// <summary>True when the chosen language differs from the one the app is running in.</summary>
    public bool NeedsRestart =>
        LanguageResolver.Resolve(Loc.Available.Select(t => t.Code), Selected.Value, CultureInfo.CurrentUICulture) != Loc.Current.Code;

    public bool ShowRestartButton => NeedsRestart && _lifetime.CanRestart;

    partial void OnSelectedChanged(LanguageOption value) => _settings.Set(LanguageResolver.SettingKey, value.Value);

    [RelayCommand]
    private void Restart() => _lifetime.Restart();
}
