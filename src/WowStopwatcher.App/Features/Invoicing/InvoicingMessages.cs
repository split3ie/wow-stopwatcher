using WowStopwatcher.Core.Invoicing;
using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.App.Features.Invoicing;

/// <summary>Localized text for the reasons and errors the Invoicing services report as codes.</summary>
internal static class InvoicingMessages
{
    /// <summary>The reason a file or preview is blocked.</summary>
    public static string Block(GenerateBlockReason reason) => reason.Kind switch
    {
        GenerateBlockKind.NoTemplate => Loc.T("invoicing.block.noTemplate"),
        GenerateBlockKind.TemplateMissing => Loc.T("invoicing.block.templateMissing"),
        GenerateBlockKind.NoConverter => Loc.T("invoicing.block.noConverter"),
        GenerateBlockKind.PlaceholderError => Loc.T(
            "invoicing.block.placeholderError",
            ("alias", reason.Alias ?? string.Empty),
            ("reason", Script(reason.ErrorCode, reason.Detail))),
        _ => string.Empty,
    };

    /// <summary>All reasons, one per line.</summary>
    public static string Blocks(IEnumerable<GenerateBlockReason> reasons) => string.Join('\n', reasons.Select(Block));

    /// <summary>The text of a placeholder script error, with the detail when there is one.</summary>
    public static string Script(ScriptErrorCode? code, string? detail)
    {
        var reason = code switch
        {
            ScriptErrorCode.Syntax => Loc.T("invoicing.scriptError.syntax"),
            ScriptErrorCode.Timeout => Loc.T("invoicing.scriptError.timeout"),
            ScriptErrorCode.Limit => Loc.T("invoicing.scriptError.limit"),
            ScriptErrorCode.Cycle => Loc.T("invoicing.scriptError.cycle"),
            _ => Loc.T("invoicing.scriptError.runtime"),
        };
        return string.IsNullOrWhiteSpace(detail) ? reason : reason + ": " + detail;
    }

    /// <summary>The text of a PDF conversion error, with the detail when there is one.</summary>
    public static string Pdf(PdfErrorCode? code, string? detail)
    {
        var reason = code switch
        {
            PdfErrorCode.NotAvailable => Loc.T("invoicing.pdfError.notAvailable"),
            PdfErrorCode.Timeout => Loc.T("invoicing.pdfError.timeout"),
            PdfErrorCode.OutputMissing => Loc.T("invoicing.pdfError.outputMissing"),
            _ => Loc.T("invoicing.pdfError.failed"),
        };
        return string.IsNullOrWhiteSpace(detail) ? reason : reason + ": " + detail;
    }

    /// <summary>The text of an alias validation code from <see cref="PlaceholderAlias.Validate"/>.</summary>
    public static string Alias(string code) => code switch
    {
        PlaceholderAlias.Empty => Loc.T("invoicing.alias.empty"),
        PlaceholderAlias.Reserved => Loc.T("invoicing.alias.reserved"),
        _ => Loc.T("invoicing.alias.invalid"),
    };
}
