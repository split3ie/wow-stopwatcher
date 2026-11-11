namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>What a template uses compared with the known names.</summary>
/// <param name="HasTemplate">True when the template file exists.</param>
/// <param name="Markers">Every distinct marker in the template.</param>
/// <param name="UnknownMarkers">Markers that are neither built-in nor a defined placeholder.</param>
/// <param name="UnusedPlaceholders">Defined placeholders the template does not use.</param>
public sealed record TemplateWarnings(
    bool HasTemplate,
    IReadOnlyList<string> Markers,
    IReadOnlyList<string> UnknownMarkers,
    IReadOnlyList<string> UnusedPlaceholders);
