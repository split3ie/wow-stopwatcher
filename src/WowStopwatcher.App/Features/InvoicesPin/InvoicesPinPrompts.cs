using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Localization;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Features.InvoicesPin;

/// <summary>The PIN dialogs (check, set, change, reset with the recovery code). Each returns true when the user completed it.</summary>
public sealed class InvoicesPinPrompts(InvoicesPinService service, IPromptDialogService prompt, IRecoveryCodeVerifier recovery)
{
    /// <summary>Asks for the current PIN. Wrong PINs and the lockout are shown in the dialog.</summary>
    public Task<bool> ConfirmCurrentAsync(string title, string confirmLabel) =>
        prompt.ShowAsync(new PromptDialogRequest(title, confirmLabel)
        {
            First = PinField(Loc.T("invoicing.pin.label")),
            Validate = async (current, _) => await CheckCurrentAsync(current),
        });

    /// <summary>Asks for a new PIN twice and stores it.</summary>
    public Task<bool> SetNewAsync() =>
        prompt.ShowAsync(new PromptDialogRequest(Loc.T("invoicing.pin.set.title"), Loc.T("invoicing.pin.set.confirm"))
        {
            Body = Loc.T("invoicing.pin.hint"),
            First = PinField(Loc.T("invoicing.pin.new")),
            Second = PinField(Loc.T("invoicing.pin.confirmNew")),
            Validate = async (first, second) =>
            {
                if (CheckNew(first, second) is { } error)
                {
                    return error;
                }

                await service.SetAsync(first);
                return null;
            },
        });

    /// <summary>Asks for the current PIN and a new one twice, then stores the new one.</summary>
    public Task<bool> ChangeAsync() =>
        prompt.ShowAsync(new PromptDialogRequest(Loc.T("invoicing.pin.change.title"), Loc.T("invoicing.pin.change.confirm"))
        {
            Body = Loc.T("invoicing.pin.hint"),
            First = PinField(Loc.T("invoicing.pin.current")),
            Second = PinField(Loc.T("invoicing.pin.new")),
            Third = PinField(Loc.T("invoicing.pin.confirmNew")),
            ValidateAll = async (current, first, second) =>
            {
                if (CheckNew(first, second) is { } error)
                {
                    return error;
                }

                if (await CheckCurrentAsync(current) is { } wrong)
                {
                    return wrong;
                }

                await service.SetAsync(first);
                return null;
            },
        });

    /// <summary>Asks for the recovery code and a new PIN twice; the new PIN is stored when the code is right.</summary>
    public Task<bool> ForgotAsync() =>
        prompt.ShowAsync(new PromptDialogRequest(Loc.T("invoicing.pin.reset.title"), Loc.T("invoicing.pin.reset.confirm"))
        {
            Body = Loc.T("invoicing.pin.reset.body"),
            First = new PromptDialogField(Loc.T("invoicing.pin.reset.code"), Mono: true),
            Second = PinField(Loc.T("invoicing.pin.new")),
            Third = PinField(Loc.T("invoicing.pin.confirmNew")),
            ValidateAll = async (code, first, second) =>
            {
                if (CheckNew(first, second) is { } error)
                {
                    return error;
                }

                if (!await recovery.VerifyAsync(code))
                {
                    return Loc.T("invoicing.pin.reset.wrongCode");
                }

                await service.SetAsync(first);
                service.ResetAttempts();
                return null;
            },
        });

    /// <summary>The "Too many tries" text for the time left (rounded up to whole seconds).</summary>
    public static string LockoutText(TimeSpan remaining) =>
        Loc.T("invoicing.pin.lockout", ("seconds", (int)Math.Ceiling(remaining.TotalSeconds)));

    private static PromptDialogField PinField(string label) => new(label, Masked: true, Mono: true);

    private async Task<string?> CheckCurrentAsync(string current)
    {
        var result = await service.VerifyAsync(current);
        return result.Status switch
        {
            PinCheckStatus.Success => null,
            PinCheckStatus.Wrong => Loc.Plural("invoicing.pin.wrong", result.RemainingTries),
            _ => LockoutText(result.RemainingLockout),
        };
    }

    private static string? CheckNew(string first, string second)
    {
        if (!PinPolicy.IsValid(first))
        {
            return Loc.T("invoicing.pin.invalid");
        }

        return first == second ? null : Loc.T("invoicing.pin.mismatch");
    }
}
