using Avalonia.Markup.Xaml;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Localization;

/// <summary>
/// XAML markup extension for translated text: <c>Text="{loc:T today.stopAll}"</c> with
/// <c>xmlns:loc="using:WowStopwatcher.App.Localization"</c>. The language is fixed for the life of the process,
/// so the value is looked up once.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
