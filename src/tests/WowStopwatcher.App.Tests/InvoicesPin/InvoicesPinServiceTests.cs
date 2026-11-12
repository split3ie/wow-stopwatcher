using WowStopwatcher.App.Features.InvoicesPin;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.App.Tests.InvoicesPin;

public class InvoicesPinServiceTests
{
    private readonly PinTestEnvironment _env = new();

    [Fact]
    public void Starts_without_a_pin()
    {
        Assert.False(_env.Service.IsEnabled);
        Assert.False(_env.Service.HasPin);
    }

    [Fact]
    public async Task Set_stores_a_hash_and_enables_the_pin()
    {
        _env.Service.Set(PinTestEnvironment.Pin);

        Assert.True(_env.Service.IsEnabled);
        Assert.Equal("true", _env.Settings.Get(InvoicesPinService.EnabledKey));
        var stored = _env.Settings.Get(InvoicesPinService.HashKey);
        Assert.NotNull(stored);
        Assert.DoesNotContain(PinTestEnvironment.Pin, stored);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("123456789")]
    [InlineData("12a456")]
    public void Set_rejects_a_pin_that_is_not_6_to_8_digits(string pin) =>
        Assert.Throws<ArgumentException>(() => _env.Service.Set(pin));

    [Fact]
    public async Task A_stored_four_digit_pin_from_an_earlier_version_still_unlocks()
    {
        _env.Settings.Set(InvoicesPinService.HashKey, PinHash.Create("1234", 1_000));
        _env.Settings.Set(InvoicesPinService.EnabledKey, "true");

        Assert.True(_env.Service.IsEnabled);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync("1234")).Status);
    }

    [Fact]
    public void A_flag_without_a_valid_hash_is_not_active()
    {
        _env.Settings.Set(InvoicesPinService.EnabledKey, "true");
        Assert.False(_env.Service.IsEnabled);

        _env.Settings.Set(InvoicesPinService.HashKey, "garbage");
        Assert.False(_env.Service.IsEnabled);
    }

    [Fact]
    public void A_hash_with_the_flag_off_is_not_active()
    {
        _env.Service.Set(PinTestEnvironment.Pin);
        _env.Settings.Set(InvoicesPinService.EnabledKey, "false");

        Assert.False(_env.Service.IsEnabled);
        Assert.True(_env.Service.HasPin);
    }

    [Fact]
    public async Task A_wrong_pin_reports_the_tries_left()
    {
        _env.EnablePin();

        var result = await _env.Service.VerifyAsync("1111");

        Assert.Equal(PinCheckStatus.Wrong, result.Status);
        Assert.Equal(PinAttemptLimiter.MaxTries - 1, result.RemainingTries);
    }

    [Fact]
    public async Task Five_wrong_pins_start_the_lockout_that_refuses_even_the_right_pin_until_it_ends()
    {
        _env.EnablePin();
        PinCheckResult last = null!;
        for (var i = 0; i < PinAttemptLimiter.MaxTries; i++)
        {
            last = await _env.Service.VerifyAsync("1111");
        }

        Assert.Equal(PinCheckStatus.LockedOut, last.Status);
        Assert.Equal(PinAttemptLimiter.LockoutDuration, last.RemainingLockout);
        Assert.Equal(PinCheckStatus.LockedOut, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);

        _env.Time.Advance(PinAttemptLimiter.LockoutDuration);

        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
    }

    [Fact]
    public async Task A_right_pin_clears_the_wrong_count()
    {
        _env.EnablePin();
        await _env.Service.VerifyAsync("1111");
        await _env.Service.VerifyAsync("1111");

        await _env.Service.VerifyAsync(PinTestEnvironment.Pin);

        Assert.Equal(PinAttemptLimiter.MaxTries, _env.Service.Limiter.RemainingTries);
    }

    [Fact]
    public void Disable_turns_the_pin_off_locks_the_state_and_clears_the_hash()
    {
        _env.EnablePin();
        _env.Service.LockState.Unlock();
        var changed = 0;
        _env.Service.Changed += (_, _) => changed++;

        _env.Service.Disable();

        Assert.False(_env.Service.IsEnabled);
        Assert.False(_env.Service.HasPin);
        Assert.True(string.IsNullOrEmpty(_env.Settings.Get(InvoicesPinService.HashKey)));
        Assert.True(_env.Service.LockState.IsLocked);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Set_again_replaces_the_pin()
    {
        _env.EnablePin();

        await _env.Service.SetAsync("987654");

        Assert.Equal(PinCheckStatus.Wrong, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync("987654")).Status);
    }
}
