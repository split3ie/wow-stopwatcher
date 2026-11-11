namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>A template copied into the app's folder.</summary>
/// <param name="FileName">Name of the app-owned copy inside the templates folder.</param>
/// <param name="OriginalName">File name the user picked, for display.</param>
/// <param name="Sha256">Lowercase SHA-256 hex of the file.</param>
public sealed record StoredTemplate(string FileName, string OriginalName, string Sha256);
