namespace WowStopwatcher.App.Shell;

/// <summary>A text field of a <see cref="PromptDialogRequest"/>.</summary>
/// <param name="Label">The caption above the field.</param>
/// <param name="Masked">True for a password (typed characters are hidden).</param>
/// <param name="Mono">True to use the monospace font (for codes).</param>
public sealed record PromptDialogField(string Label, bool Masked = false, bool Mono = false);
