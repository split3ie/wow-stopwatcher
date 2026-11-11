namespace WowStopwatcher.Core.Invoicing;

/// <summary>
/// A user-defined template placeholder of a company.
/// </summary>
/// <param name="Id">Database id; 0 for a new one.</param>
/// <param name="CompanyId">Owning company.</param>
/// <param name="Alias">Name used in the template as <c>{{alias}}</c> (see <see cref="PlaceholderAlias"/>).</param>
/// <param name="Kind">Static text, script, or automatic (the built-in value of the alias).</param>
/// <param name="Value">The text, or the script code.</param>
/// <param name="Hidden">True when the result is shown as <see cref="MoneyMask.Mask"/> while amounts are hidden.</param>
/// <param name="SortOrder">Position in lists; lower comes first.</param>
public sealed record PlaceholderDefinition(
    long Id,
    string CompanyId,
    string Alias,
    PlaceholderKind Kind,
    string Value,
    bool Hidden,
    int SortOrder);
