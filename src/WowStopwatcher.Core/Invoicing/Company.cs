namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// A company that is billed for the time of the stopwatches assigned to it. Deleted companies are kept (soft delete) so their invoices stay.
/// </summary>
/// <param name="Id">Stable identifier (see <see cref="NewId"/>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Color">Hex color such as "#2F6FEB".</param>
/// <param name="SortOrder">Position in lists; lower comes first.</param>
/// <param name="Rate">Hourly rate in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO 4217 currency code.</param>
/// <param name="TemplateFile">File name of the app-owned copy of the DOCX template (<c>&lt;companyId&gt;-&lt;8 hex&gt;.docx</c>), relative to the <c>templates</c> folder next to the database; null when no template is chosen.</param>
/// <param name="TemplateName">Original template file name, for display.</param>
/// <param name="TemplateSha256">Lowercase SHA-256 hex of the template file.</param>
/// <param name="FileNamePattern">Output file name with <c>{{name}}</c> markers (see <see cref="InvoiceFileName"/>).</param>
/// <param name="AskDestination">True to ask where to save each generated file; false to use <paramref name="OutputFolder"/>.</param>
/// <param name="OutputFolder">Fixed output folder; used when <paramref name="AskDestination"/> is false.</param>
/// <param name="NextInvoiceNumber">The number the next invoice marked as sent receives (shown on generated files until then).</param>
/// <param name="DayStartMinutes">Default start of an invoice day, minutes after local midnight (0 to 1439); 0 is plain calendar days.</param>
/// <param name="IsDeleted">True when the company was deleted.</param>
/// <param name="DateFormat">A .NET custom date format such as "dd.MM.yyyy" for the date values of the template and file name; null or empty uses the app language's short date.</param>
public sealed record Company(
    string Id,
    string Name,
    string Color,
    int SortOrder,
    decimal Rate,
    string Currency,
    string? TemplateFile,
    string? TemplateName,
    string? TemplateSha256,
    string FileNamePattern,
    bool AskDestination,
    string? OutputFolder,
    int NextInvoiceNumber,
    bool IsDeleted = false,
    int DayStartMinutes = 0,
    string? DateFormat = null)
{
    /// <summary>Creates a new unique company id.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");
}
