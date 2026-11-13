using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Core.Tests.Security;

public sealed class PinTests
{
    [Theory]
    [InlineData("123456", true)]
    [InlineData("12345678", true)]
    [InlineData("000000", true)]
    [InlineData("1234", false)]
    [InlineData("12345", false)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    [InlineData("12 4", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PinPolicy_AcceptsOnlySixToEightAsciiDigits(string? pin, bool expected) =>
        Assert.Equal(expected, PinPolicy.IsValid(pin));

    [Fact]
    public void PinHash_VerifiesCorrectPinAndRejectsOthers()
    {
        var stored = PinHash.Create("1234", 1000);

        Assert.StartsWith("pbkdf2-sha256$1000$", stored);
        Assert.True(PinHash.Verify("1234", stored));
        Assert.False(PinHash.Verify("1235", stored));
        Assert.False(PinHash.Verify(null, stored));
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("98765")]
    public void PinHash_StillVerifiesPinsShorterThanThePolicyMinimum(string oldPin)
    {
        Assert.False(PinPolicy.IsValid(oldPin));
        Assert.True(PinHash.Verify(oldPin, PinHash.Create(oldPin, 1000)));
    }

    [Fact]
    public void PinHash_UsesRandomSalt() =>
        Assert.NotEqual(PinHash.Create("1234", 10), PinHash.Create("1234", 10));

    [Fact]
    public void PinHash_DefaultIterationsAreWritten() =>
        Assert.StartsWith("pbkdf2-sha256$600000$", PinHash.Create("1234"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$1000$AAAA")]
    [InlineData("md5$1000$AAAA$BBBB")]
    [InlineData("pbkdf2-sha256$x$AAAA$BBBB")]
    [InlineData("pbkdf2-sha256$0$AAAA$BBBB")]
    [InlineData("pbkdf2-sha256$-5$AAAA$BBBB")]
    [InlineData("pbkdf2-sha256$1000$!!!$???")]
    [InlineData("pbkdf2-sha256$1000$AAAA$BBBB")]
    public void PinHash_MalformedStoredValueVerifiesFalse(string? stored) =>
        Assert.False(PinHash.Verify("1234", stored));

    [Fact]
    public void PinHash_RefusesHugeIterationCountWithoutDeriving()
    {
        var valid = PinHash.Create("1234", 10).Split('$');
        var tampered = $"{valid[0]}${PinHash.MaxIterations + 1}${valid[2]}${valid[3]}";
        var huge = $"{valid[0]}$2147483647${valid[2]}${valid[3]}";

        Assert.False(PinHash.Verify("1234", tampered));
        Assert.False(PinHash.Verify("1234", huge));
        Assert.Throws<ArgumentOutOfRangeException>(() => PinHash.Create("1234", PinHash.MaxIterations + 1));
    }

    [Fact]
    public void Limiter_CountsDownTriesAndLocksOutAfterFive()
    {
        var clock = new FakeTimeProvider();
        var limiter = new PinAttemptLimiter(clock);

        Assert.Equal(5, limiter.RemainingTries);
        limiter.RecordFailure();
        Assert.Equal(4, limiter.RemainingTries);
        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure();
        }

        Assert.True(limiter.IsLockedOut);
        Assert.Equal(0, limiter.RemainingTries);
        Assert.Equal(TimeSpan.FromSeconds(30), limiter.RemainingLockout);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(20), limiter.RemainingLockout);
    }

    [Fact]
    public void Limiter_FailuresDuringLockoutDoNotExtendIt()
    {
        var clock = new FakeTimeProvider();
        var limiter = new PinAttemptLimiter(clock);
        for (var i = 0; i < 5; i++)
        {
            limiter.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(20));
        limiter.RecordFailure();

        Assert.Equal(TimeSpan.FromSeconds(10), limiter.RemainingLockout);
    }

    [Fact]
    public void Limiter_FreshSetOfFiveTriesAfterLockoutEnds()
    {
        var clock = new FakeTimeProvider();
        var limiter = new PinAttemptLimiter(clock);
        for (var i = 0; i < 5; i++)
        {
            limiter.RecordFailure();
        }

        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.False(limiter.IsLockedOut);
        Assert.Equal(TimeSpan.Zero, limiter.RemainingLockout);
        Assert.Equal(5, limiter.RemainingTries);
        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure();
        }

        Assert.False(limiter.IsLockedOut);
        limiter.RecordFailure();
        Assert.True(limiter.IsLockedOut);
    }

    [Fact]
    public void Limiter_SuccessResetsCount()
    {
        var limiter = new PinAttemptLimiter(new FakeTimeProvider());
        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure();
        }

        limiter.RecordSuccess();

        Assert.Equal(5, limiter.RemainingTries);
    }

    [Fact]
    public void LockState_StartsLockedAndUnlockLockRaiseChanged()
    {
        var state = new PinLockState(new FakeTimeProvider());
        var changes = 0;
        state.Changed += (_, _) => changes++;

        Assert.True(state.IsLocked);
        state.Unlock();
        state.Unlock();
        Assert.False(state.IsLocked);
        Assert.Equal(1, changes);
        state.Lock();
        state.Lock();
        Assert.True(state.IsLocked);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void LockState_LocksAfterFiveIdleMinutes()
    {
        var clock = new FakeTimeProvider();
        var state = new PinLockState(clock);
        var changes = 0;
        state.Changed += (_, _) => changes++;
        state.Unlock();

        clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.False(state.IsLocked);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(state.IsLocked);
        Assert.Equal(1, changes);
        state.CheckIdle();
        Assert.Equal(2, changes);
        state.CheckIdle();
        Assert.Equal(2, changes);
        Assert.True(state.IsLocked);
    }

    [Fact]
    public void LockState_ActivityRestartsIdleTimer()
    {
        var clock = new FakeTimeProvider();
        var state = new PinLockState(clock);
        state.Unlock();

        clock.Advance(TimeSpan.FromMinutes(4));
        state.RecordActivity();
        clock.Advance(TimeSpan.FromMinutes(4));

        Assert.False(state.IsLocked);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(state.IsLocked);
    }

    [Fact]
    public void LockState_ActivityAfterTimeoutLocksAndActivityWhileLockedDoesNothing()
    {
        var clock = new FakeTimeProvider();
        var state = new PinLockState(clock);
        state.RecordActivity();
        Assert.True(state.IsLocked);

        state.Unlock();
        clock.Advance(TimeSpan.FromMinutes(6));
        state.RecordActivity();

        Assert.True(state.IsLocked);
        state.Unlock();
        Assert.False(state.IsLocked);
    }
}
