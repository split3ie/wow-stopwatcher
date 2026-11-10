namespace WowStopwatcher.App.Features.Settings;

/// <summary>One entry of the language picker: the setting value ("system" or a language code) and its label.</summary>
public sealed record LanguageOption(string Value, string Label);
