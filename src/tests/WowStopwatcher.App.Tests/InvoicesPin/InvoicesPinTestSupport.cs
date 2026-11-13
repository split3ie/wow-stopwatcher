using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Features.InvoicesPin;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.Tests.Invoicing;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Tests.InvoicesPin;

/// <summary>Shared fakes for the Invoices PIN tests.</summary>
internal sealed class PinTestEnvironment
{
    public const string Pin = "258012";

    public PinTestEnvironment()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        Settings = new FakeSettingsRepository();
        Service = new InvoicesPinService(Settings, Time, hashIterations: 1_000);
        Prompt = new FakePromptDialogService();
        Recovery = new FakeRecoveryCodeVerifier();
        Prompts = new InvoicesPinPrompts(Service, Prompt, Recovery);
    }

    public FakeTimeProvider Time { get; }

    public FakeSettingsRepository Settings { get; }

    public InvoicesPinService Service { get; }

    public FakePromptDialogService Prompt { get; }

    public FakeRecoveryCodeVerifier Recovery { get; }

    public InvoicesPinPrompts Prompts { get; }

    /// <summary>Enables the PIN <see cref="Pin"/> and unlocks nothing.</summary>
    public void EnablePin() => Service.Set(Pin);
}

/// <summary>Runs the validation of a prompt with scripted input instead of showing a dialog.</summary>
internal sealed class FakePromptDialogService : IPromptDialogService
{
    private readonly Queue<string[][]> _scripts = new();

    public List<PromptDialogRequest> Requests { get; } = [];

    /// <summary>Error texts the validation gave, in order.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Queues a dialog: the attempts the user makes (field texts), then the dialog is cancelled unless one is accepted.</summary>
    public void Script(params string[][] attempts) => _scripts.Enqueue(attempts);

    public async Task<bool> ShowAsync(PromptDialogRequest request)
    {
        Requests.Add(request);
        var attempts = _scripts.Count > 0 ? _scripts.Dequeue() : [];
        foreach (var attempt in attempts)
        {
            var first = attempt.ElementAtOrDefault(0) ?? string.Empty;
            var second = attempt.ElementAtOrDefault(1) ?? string.Empty;
            var third = attempt.ElementAtOrDefault(2) ?? string.Empty;
            string? error = null;
            if (request.ValidateAll is { } validateAll)
            {
                error = await validateAll(first, second, third);
            }
            else if (request.Validate is { } validate)
            {
                error = await validate(first, second);
            }

            if (error is null)
            {
                return true;
            }

            Errors.Add(error);
        }

        return false;
    }
}

internal sealed class FakeRecoveryCodeVerifier : IRecoveryCodeVerifier
{
    public string ValidCode { get; set; } = "7KQ2-M9XD-4TPA-HV3C-R8NE-W6JF";

    public Task<bool> VerifyAsync(string code) => Task.FromResult(RecoveryCode.Normalize(code) == RecoveryCode.Normalize(ValidCode));
}

internal sealed class PinImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();

    public bool CheckAccess() => true;
}
