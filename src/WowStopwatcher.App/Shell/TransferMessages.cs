using WowStopwatcher.Core.Localization;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Shell;

/// <summary>Localized texts for the results of database transfers (copy, export, import, pending steps).</summary>
public static class TransferMessages
{
    /// <summary>The reason a transfer failed, in the app language.</summary>
    public static string Describe(TransferErrorCode code) => Loc.T(code switch
    {
        TransferErrorCode.SourceMissing => "transfer.error.sourceMissing",
        TransferErrorCode.TargetExists => "transfer.error.targetExists",
        TransferErrorCode.CopyFailed => "transfer.error.copyFailed",
        TransferErrorCode.BackupFailed => "transfer.error.backupFailed",
        TransferErrorCode.NotAZip => "transfer.error.notAZip",
        TransferErrorCode.NoDatabase => "transfer.error.noDatabase",
        TransferErrorCode.Damaged => "transfer.error.damaged",
        TransferErrorCode.NewerVersion => "transfer.error.newerVersion",
        _ => "transfer.error.io",
    });

    /// <summary>The title and body of the message shown once after a pending step ran at startup.</summary>
    public static (string Title, string Body) DescribeOutcome(PendingOutcome outcome)
    {
        if (outcome.Error is { } error)
        {
            return (Loc.T("data.outcome.failedTitle"), Loc.T("data.outcome.failedBody", ("reason", Describe(error))));
        }

        return (Loc.T("data.outcome.successTitle"), Loc.T(outcome.Kind switch
        {
            PendingKind.Import => "data.outcome.imported",
            PendingKind.Replace => "data.outcome.replaced",
            _ => "data.outcome.moved",
        }));
    }
}
