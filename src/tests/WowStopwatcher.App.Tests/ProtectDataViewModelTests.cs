using Microsoft.Extensions.Time.Testing;
using WowStopwatcher.App.Shell;
using WowStopwatcher.App.ViewModels;

namespace WowStopwatcher.App.Tests;

public sealed class ProtectDataViewModelTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; set; }

        public bool CanRead { get; set; } = true;

        public int Clears { get; private set; }

        public Task<bool> SetTextAsync(string text)
        {
            Text = text;
            return Task.FromResult(true);
        }

        public Task<string?> TryGetTextAsync() => Task.FromResult(CanRead ? Text : null);

        public Task ClearAsync()
        {
            Text = null;
            Clears++;
            return Task.CompletedTask;
        }
    }

    private const string Code = "7KQ2-M9XD-4TPA-HV3C-R8NE-W6JF";

    private static ProtectDataViewModel Create(bool pinAvailable, Func<string?, Task> commit, FakeClipboard? clipboard = null, TimeProvider? time = null) =>
        new(new ProtectDataRequest(pinAvailable, Code, false, commit), clipboard ?? new FakeClipboard(), time);

    /// <summary>Gives the timer continuation (on the thread pool) time to run.</summary>
    private static Task Settle() => Task.Delay(200);

    [Fact]
    public async Task CopiedCode_IsClearedAfterSixtySeconds_WhenStillOnTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var time = new FakeTimeProvider();
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard, time);

        await viewModel.CopyCommand.ExecuteAsync(null);
        time.Advance(TimeSpan.FromSeconds(59));
        await Settle();
        Assert.Equal(Code, clipboard.Text);

        time.Advance(TimeSpan.FromSeconds(1));
        await Settle();

        Assert.Null(clipboard.Text);
        Assert.Equal(1, clipboard.Clears);
    }

    [Fact]
    public async Task CopiedCode_IsKept_WhenTheClipboardHoldsSomethingElse()
    {
        var clipboard = new FakeClipboard();
        var time = new FakeTimeProvider();
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard, time);
        await viewModel.CopyCommand.ExecuteAsync(null);
        clipboard.Text = "something the user copied later";

        time.Advance(TimeSpan.FromSeconds(60));
        await Settle();

        Assert.Equal("something the user copied later", clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
    }

    [Fact]
    public async Task CopiedCode_IsKept_WhenTheClipboardCannotBeRead()
    {
        var clipboard = new FakeClipboard { CanRead = false };
        var time = new FakeTimeProvider();
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard, time);
        await viewModel.CopyCommand.ExecuteAsync(null);

        time.Advance(TimeSpan.FromSeconds(60));
        await Settle();

        Assert.Equal(Code, clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
    }

    [Fact]
    public async Task ClosingTheWindow_ClearsTheCopiedCode_AndTheTimerDoesNotClearAgain()
    {
        var clipboard = new FakeClipboard();
        var time = new FakeTimeProvider();
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard, time);
        await viewModel.CopyCommand.ExecuteAsync(null);

        await viewModel.ClearCopiedCodeAsync();
        time.Advance(TimeSpan.FromSeconds(120));
        await Settle();

        Assert.Null(clipboard.Text);
        Assert.Equal(1, clipboard.Clears);
    }

    [Fact]
    public async Task ClosingWithoutCopying_DoesNotTouchTheClipboard()
    {
        var clipboard = new FakeClipboard { Text = Code };
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard);

        await viewModel.ClearCopiedCodeAsync();

        Assert.Equal(Code, clipboard.Text);
        Assert.Equal(0, clipboard.Clears);
    }

    [Fact]
    public void WithoutPinSetup_StartsOnTheRecoveryCodeStep()
    {
        var viewModel = Create(false, _ => Task.CompletedTask);

        Assert.True(viewModel.IsCodeStep);
        Assert.False(viewModel.EncryptCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("123456789", "123456789")]
    [InlineData("12a4", "12a4")]
    [InlineData("12345", "12345")]
    [InlineData("1234", "4321")]
    public void InvalidOrDifferentPins_StayOnTheFirstStep(string pin, string confirm)
    {
        var viewModel = Create(true, _ => Task.CompletedTask);
        viewModel.Pin = pin;
        viewModel.ConfirmPin = confirm;

        viewModel.ContinueCommand.Execute(null);

        Assert.True(viewModel.IsPinStep);
        Assert.True(viewModel.HasPinError);
    }

    [Fact]
    public void MatchingPins_MoveToTheRecoveryCodeStep()
    {
        var viewModel = Create(true, _ => Task.CompletedTask);
        viewModel.Pin = "258012";
        viewModel.ConfirmPin = "258012";

        viewModel.ContinueCommand.Execute(null);

        Assert.True(viewModel.IsCodeStep);
        Assert.False(viewModel.HasPinError);
    }

    [Fact]
    public void DontUseAPin_SkipsValidation()
    {
        var viewModel = Create(true, _ => Task.CompletedTask);
        viewModel.NoPin = true;

        viewModel.ContinueCommand.Execute(null);

        Assert.True(viewModel.IsCodeStep);
    }

    [Fact]
    public async Task Encrypt_NeedsTheCheckboxAndPassesThePin()
    {
        string? receivedPin = "unset";
        var viewModel = Create(true, pin =>
        {
            receivedPin = pin;
            return Task.CompletedTask;
        });
        viewModel.Pin = "258012";
        viewModel.ConfirmPin = "258012";
        viewModel.ContinueCommand.Execute(null);
        Exception? finishedWith = new InvalidOperationException("not finished");
        viewModel.Finished += (_, error) => finishedWith = error;

        Assert.False(viewModel.EncryptCommand.CanExecute(null));
        viewModel.Saved = true;
        Assert.True(viewModel.EncryptCommand.CanExecute(null));
        await viewModel.EncryptCommand.ExecuteAsync(null);

        Assert.Equal("258012", receivedPin);
        Assert.Null(finishedWith);
    }

    [Fact]
    public async Task Encrypt_WithNoPinChoice_PassesNull()
    {
        string? receivedPin = "unset";
        var viewModel = Create(true, pin =>
        {
            receivedPin = pin;
            return Task.CompletedTask;
        });
        viewModel.NoPin = true;
        viewModel.Saved = true;

        await viewModel.EncryptCommand.ExecuteAsync(null);

        Assert.Null(receivedPin);
    }

    [Fact]
    public async Task Encrypt_Failure_IsReportedThroughFinished()
    {
        var viewModel = Create(false, _ => throw new IOException("disk full"));
        viewModel.Saved = true;
        Exception? finishedWith = null;
        viewModel.Finished += (_, error) => finishedWith = error;

        await viewModel.EncryptCommand.ExecuteAsync(null);

        Assert.IsType<IOException>(finishedWith);
    }

    [Fact]
    public async Task Copy_PutsTheCodeOnTheClipboard()
    {
        var clipboard = new FakeClipboard();
        var viewModel = Create(false, _ => Task.CompletedTask, clipboard);

        await viewModel.CopyCommand.ExecuteAsync(null);

        Assert.Equal("7KQ2-M9XD-4TPA-HV3C-R8NE-W6JF", clipboard.Text);
    }
}
