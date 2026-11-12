using WowStopwatcher.App.Features.InvoicesPin;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Tests.InvoicesPin;

public class InvoicesPinFlowTests : IDisposable
{
    private const string NewPin = "739104";

    private readonly PinTestEnvironment _env = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var item in _disposables)
        {
            item.Dispose();
        }
    }

    // Startup step

    [Fact]
    public void Startup_setup_is_available_and_stores_the_chosen_pin()
    {
        var setup = new InvoicesStartupPinSetup(_env.Service);

        setup.Apply(PinTestEnvironment.Pin);

        Assert.True(_env.Service.IsEnabled);
        Assert.Equal("true", _env.Settings.Get(InvoicesPinService.EnabledKey));
    }

    [Fact]
    public void Startup_setup_without_a_pin_disables_it()
    {
        _env.EnablePin();

        new InvoicesStartupPinSetup(_env.Service).Apply(null);

        Assert.False(_env.Service.IsEnabled);
        Assert.Equal("false", _env.Settings.Get(InvoicesPinService.EnabledKey));
        Assert.False(_env.Service.HasPin);
    }

    // Sensitive action guard

    [Fact]
    public async Task Guard_allows_everything_when_no_pin_is_active()
    {
        var guard = new InvoicesSensitiveActionGuard(_env.Service, _env.Prompts);

        Assert.True(await guard.ConfirmAsync());
        Assert.Empty(_env.Prompt.Requests);
    }

    [Fact]
    public async Task Guard_allows_the_action_after_the_right_pin()
    {
        _env.EnablePin();
        _env.Prompt.Script([PinTestEnvironment.Pin]);

        Assert.True(await new InvoicesSensitiveActionGuard(_env.Service, _env.Prompts).ConfirmAsync());
    }

    [Fact]
    public async Task Guard_refuses_when_the_dialog_is_cancelled()
    {
        _env.EnablePin();
        _env.Prompt.Script();

        Assert.False(await new InvoicesSensitiveActionGuard(_env.Service, _env.Prompts).ConfirmAsync());
    }

    [Fact]
    public async Task Guard_shows_the_tries_left_for_a_wrong_pin_and_accepts_a_retry()
    {
        _env.EnablePin();
        _env.Prompt.Script(["1111"], [PinTestEnvironment.Pin]);

        Assert.True(await new InvoicesSensitiveActionGuard(_env.Service, _env.Prompts).ConfirmAsync());
        Assert.Contains("4", Assert.Single(_env.Prompt.Errors));
    }

    [Fact]
    public async Task Guard_shows_the_lockout_after_too_many_wrong_pins()
    {
        _env.EnablePin();
        _env.Prompt.Script(Enumerable.Repeat(new[] { "1111" }, PinAttemptLimiter.MaxTries).Append([PinTestEnvironment.Pin]).ToArray());

        var allowed = await new InvoicesSensitiveActionGuard(_env.Service, _env.Prompts).ConfirmAsync();

        Assert.False(allowed);
        Assert.Equal(PinAttemptLimiter.MaxTries + 1, _env.Prompt.Errors.Count);
        Assert.Contains("30", _env.Prompt.Errors[^1]);
    }

    // Prompts

    [Fact]
    public async Task Set_new_pin_rejects_a_mismatch_and_a_short_pin_then_stores_a_good_one()
    {
        _env.Prompt.Script(["12", "12"], ["258012", "258013"], [NewPin, NewPin]);

        Assert.True(await _env.Prompts.SetNewAsync());

        Assert.Equal(2, _env.Prompt.Errors.Count);
        Assert.True(_env.Service.IsEnabled);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);
    }

    [Fact]
    public async Task Change_pin_needs_the_current_pin()
    {
        _env.EnablePin();
        _env.Prompt.Script(["1111", NewPin, NewPin]);

        Assert.False(await _env.Prompts.ChangeAsync());
        Assert.Single(_env.Prompt.Errors);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
    }

    [Fact]
    public async Task Change_pin_stores_the_new_pin_after_the_right_current_pin()
    {
        _env.EnablePin();
        _env.Prompt.Script([PinTestEnvironment.Pin, NewPin, NewPin]);

        Assert.True(await _env.Prompts.ChangeAsync());

        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);
    }

    [Fact]
    public async Task Change_pin_does_not_spend_a_try_when_the_new_pin_is_invalid()
    {
        _env.EnablePin();
        _env.Prompt.Script(["1111", "12", "12"]);

        await _env.Prompts.ChangeAsync();

        Assert.Equal(PinAttemptLimiter.MaxTries, _env.Service.Limiter.RemainingTries);
    }

    [Fact]
    public async Task Forgot_pin_with_the_right_code_sets_the_new_pin_and_clears_the_wrong_count()
    {
        _env.EnablePin();
        await _env.Service.VerifyAsync("1111");
        _env.Prompt.Script([_env.Recovery.ValidCode, NewPin, NewPin]);

        Assert.True(await _env.Prompts.ForgotAsync());

        Assert.Equal(PinAttemptLimiter.MaxTries, _env.Service.Limiter.RemainingTries);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);
    }

    [Fact]
    public async Task Forgot_pin_with_a_wrong_code_keeps_the_old_pin()
    {
        _env.EnablePin();
        _env.Prompt.Script(["0000-0000-0000-0000-0000-0000", NewPin, NewPin]);

        Assert.False(await _env.Prompts.ForgotAsync());

        Assert.Single(_env.Prompt.Errors);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
    }

    // Lock card

    private InvoicesLockViewModel CreateLock()
    {
        var vm = new InvoicesLockViewModel(_env.Service, _env.Prompts, _env.Time, new PinImmediateDispatcher());
        _disposables.Add(vm);
        return vm;
    }

    [Fact]
    public void The_page_is_locked_only_while_a_pin_is_active()
    {
        var lockPage = CreateLock();
        Assert.False(lockPage.IsLocked);

        _env.EnablePin();
        Assert.True(lockPage.IsLocked);
    }

    [Fact]
    public async Task Unlock_with_the_right_pin_opens_the_page()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        lockPage.Pin = PinTestEnvironment.Pin;

        await lockPage.UnlockCommand.ExecuteAsync(null);

        Assert.False(lockPage.IsLocked);
        Assert.Equal(string.Empty, lockPage.Pin);
        Assert.False(lockPage.HasError);
    }

    [Fact]
    public async Task A_stored_four_digit_pin_still_unlocks_the_page()
    {
        _env.Settings.Set(InvoicesPinService.HashKey, PinHash.Create("1234", 1_000));
        _env.Settings.Set(InvoicesPinService.EnabledKey, "true");
        var lockPage = CreateLock();
        lockPage.Pin = "1234";
        Assert.True(lockPage.UnlockCommand.CanExecute(null));

        await lockPage.UnlockCommand.ExecuteAsync(null);

        Assert.False(lockPage.IsLocked);
    }

    [Fact]
    public async Task A_wrong_pin_shows_the_tries_left_and_keeps_the_page_locked()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        lockPage.Pin = "1111";

        await lockPage.UnlockCommand.ExecuteAsync(null);

        Assert.True(lockPage.IsLocked);
        Assert.True(lockPage.HasError);
        Assert.Contains("4", lockPage.Error);
        Assert.False(lockPage.IsLockedOut);
    }

    [Fact]
    public async Task Typing_clears_the_error()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        lockPage.Pin = "1111";
        await lockPage.UnlockCommand.ExecuteAsync(null);

        lockPage.Pin = "11112";

        Assert.False(lockPage.HasError);
    }

    [Fact]
    public async Task Too_many_wrong_pins_show_a_live_countdown_that_ends()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        for (var i = 0; i < PinAttemptLimiter.MaxTries; i++)
        {
            lockPage.Pin = "1111";
            await lockPage.UnlockCommand.ExecuteAsync(null);
        }

        Assert.True(lockPage.IsLockedOut);
        Assert.Contains("30", lockPage.LockoutText);
        lockPage.Pin = PinTestEnvironment.Pin;
        Assert.False(lockPage.UnlockCommand.CanExecute(null));

        _env.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(lockPage.IsLockedOut);
        Assert.Contains("20", lockPage.LockoutText);

        _env.Time.Advance(TimeSpan.FromSeconds(20));
        Assert.False(lockPage.IsLockedOut);
        Assert.Equal(string.Empty, lockPage.LockoutText);
        Assert.True(lockPage.UnlockCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_lockout_started_elsewhere_shows_when_the_card_opens()
    {
        _env.EnablePin();
        for (var i = 0; i < PinAttemptLimiter.MaxTries; i++)
        {
            await _env.Service.VerifyAsync("1111");
        }

        Assert.True(CreateLock().IsLockedOut);
    }

    [Fact]
    public async Task Forgot_pin_on_the_card_opens_the_page_after_a_reset()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        _env.Prompt.Script([_env.Recovery.ValidCode, NewPin, NewPin]);

        await lockPage.ForgotCommand.ExecuteAsync(null);

        Assert.False(lockPage.IsLocked);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);
    }

    [Fact]
    public async Task Cancelling_forgot_pin_keeps_the_page_locked()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        _env.Prompt.Script();

        await lockPage.ForgotCommand.ExecuteAsync(null);

        Assert.True(lockPage.IsLocked);
    }

    // Re-lock

    private async Task<InvoicesLockViewModel> UnlockedPageAsync()
    {
        _env.EnablePin();
        var lockPage = CreateLock();
        lockPage.Pin = PinTestEnvironment.Pin;
        await lockPage.UnlockCommand.ExecuteAsync(null);
        Assert.False(lockPage.IsLocked);
        return lockPage;
    }

    [Fact]
    public async Task Hiding_the_window_locks_the_page()
    {
        var lockPage = await UnlockedPageAsync();
        var autoLock = new InvoicesAutoLock(_env.Service, _env.Time, new PinImmediateDispatcher());
        _disposables.Add(autoLock);

        autoLock.SetWindowVisible(true);
        Assert.False(lockPage.IsLocked);

        autoLock.SetWindowVisible(false);
        Assert.True(lockPage.IsLocked);
    }

    [Fact]
    public async Task The_timer_locks_the_page_after_5_idle_minutes()
    {
        var lockPage = await UnlockedPageAsync();
        var autoLock = new InvoicesAutoLock(_env.Service, _env.Time, new PinImmediateDispatcher());
        _disposables.Add(autoLock);
        autoLock.Start();

        _env.Time.Advance(TimeSpan.FromMinutes(4));
        Assert.False(lockPage.IsLocked);

        _env.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(lockPage.IsLocked);
    }

    [Fact]
    public async Task Activity_keeps_the_page_unlocked()
    {
        var lockPage = await UnlockedPageAsync();
        var autoLock = new InvoicesAutoLock(_env.Service, _env.Time, new PinImmediateDispatcher());
        _disposables.Add(autoLock);
        autoLock.Start();

        for (var i = 0; i < 4; i++)
        {
            _env.Time.Advance(TimeSpan.FromMinutes(3));
            lockPage.RecordActivity();
        }

        Assert.False(lockPage.IsLocked);

        _env.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(lockPage.IsLocked);
    }

    [Fact]
    public async Task Input_after_the_idle_time_locks_before_the_timer_runs()
    {
        var lockPage = await UnlockedPageAsync();

        _env.Time.Advance(TimeSpan.FromMinutes(6));
        lockPage.RecordActivity();

        Assert.True(lockPage.IsLocked);
    }
}
