using WowStopwatcher.Core.Invoicing;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>One reason a file or preview cannot be made.</summary>
/// <param name="Kind">The reason.</param>
/// <param name="Alias">The placeholder, for <see cref="GenerateBlockKind.PlaceholderError"/>.</param>
/// <param name="Detail">The error detail of the placeholder.</param>
/// <param name="ErrorCode">The error code of the placeholder, for localized text.</param>
public sealed record GenerateBlockReason(GenerateBlockKind Kind, string? Alias = null, string? Detail = null, ScriptErrorCode? ErrorCode = null);
