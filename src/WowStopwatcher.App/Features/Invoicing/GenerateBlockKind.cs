namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Why a file or preview cannot be made.</summary>
public enum GenerateBlockKind
{
    /// <summary>The company has no template.</summary>
    NoTemplate,

    /// <summary>The template file of the company is missing on disk.</summary>
    TemplateMissing,

    /// <summary>Neither Word nor LibreOffice is installed.</summary>
    NoConverter,

    /// <summary>A placeholder used by the template has an error.</summary>
    PlaceholderError,
}
