using WowStopwatcher.App.Features.InvoicesPin;
using WowStopwatcher.App.Shell;
using WowStopwatcher.Core.Security;
using WowStopwatcher.Data;

namespace WowStopwatcher.App.Tests.InvoicesPin;

public sealed class InvoicesPinSettingsTests : IDisposable
{
    private const string NewPin = "739104";

    private readonly PinTestEnvironment _env = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wowstopwatcher-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private InvoicesPinSettingsViewModel CreateCard() => new(_env.Service, _env.Prompts);

    [Fact]
    public void The_toggle_reflects_the_stored_state()
    {
        Assert.False(CreateCard().IsEnabled);

        _env.EnablePin();
        Assert.True(CreateCard().IsEnabled);
    }

    [Fact]
    public async Task Turning_on_without_a_pin_asks_for_a_new_one()
    {
        var card = CreateCard();
        _env.Prompt.Script([NewPin, NewPin]);

        await card.ApplyToggleAsync(true);

        Assert.True(card.IsEnabled);
        Assert.True(_env.Service.IsEnabled);
        Assert.Single(_env.Prompt.Requests);
    }

    [Fact]
    public async Task Cancelling_the_new_pin_turns_the_toggle_back_off()
    {
        var card = CreateCard();
        _env.Prompt.Script();

        await card.ApplyToggleAsync(true);

        Assert.False(card.IsEnabled);
        Assert.False(_env.Service.IsEnabled);
    }

    [Fact]
    public async Task Turning_on_after_turning_off_asks_for_a_new_pin_again()
    {
        _env.EnablePin();
        _env.Service.Disable();
        var card = CreateCard();
        _env.Prompt.Script([NewPin, NewPin]);

        await card.ApplyToggleAsync(true);

        Assert.Single(_env.Prompt.Requests);
        Assert.True(card.IsEnabled);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);
        Assert.Equal(PinCheckStatus.Wrong, (await _env.Service.VerifyAsync(PinTestEnvironment.Pin)).Status);
    }

    [Fact]
    public async Task Turning_off_asks_for_the_current_pin()
    {
        _env.EnablePin();
        var card = CreateCard();
        _env.Prompt.Script([PinTestEnvironment.Pin]);

        await card.ApplyToggleAsync(false);

        Assert.False(card.IsEnabled);
        Assert.False(_env.Service.IsEnabled);
    }

    [Fact]
    public async Task A_wrong_or_cancelled_pin_keeps_it_on()
    {
        _env.EnablePin();
        var card = CreateCard();
        _env.Prompt.Script(["1111"]);

        await card.ApplyToggleAsync(false);

        Assert.True(card.IsEnabled);
        Assert.True(_env.Service.IsEnabled);
    }

    [Fact]
    public async Task Change_pin_and_forgot_pin_open_their_dialogs()
    {
        _env.EnablePin();
        var card = CreateCard();
        _env.Prompt.Script([PinTestEnvironment.Pin, NewPin, NewPin]);
        _env.Prompt.Script([_env.Recovery.ValidCode, "135790", "135790"]);

        await card.ChangeCommand.ExecuteAsync(null);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync(NewPin)).Status);

        await card.ForgotCommand.ExecuteAsync(null);
        Assert.Equal(PinCheckStatus.Success, (await _env.Service.VerifyAsync("135790")).Status);
    }

    // The recovery code check against the key file next to the database.

    private sealed class FixedKeys(DatabaseKey? key) : IDatabaseKeyProvider
    {
        public DatabaseKey? Key => key;

        public Guid? DatabaseId => null;
    }

    private (DatabaseRecoveryCodeVerifier Verifier, string Code) CreateVerifier(bool matchingKey = true, bool writeFile = true)
    {
        Directory.CreateDirectory(_folder);
        var databasePath = Path.Combine(_folder, "wowstopwatcher.db");
        var key = DatabaseKey.Generate();
        var code = RecoveryCode.Generate();
        if (writeFile)
        {
            var wrapped = DatabaseKeyWrapper.Wrap(key, code, iterations: 1_000);
            DatabaseKeyFile.Write(databasePath, new DatabaseKeyFileContents(Guid.NewGuid(), wrapped));
        }

        var database = new SqliteDatabase(databasePath);
        return (new DatabaseRecoveryCodeVerifier(database, new FixedKeys(matchingKey ? key : DatabaseKey.Generate())), code);
    }

    [Fact]
    public async Task The_right_recovery_code_is_accepted_in_any_spelling()
    {
        var (verifier, code) = CreateVerifier();

        Assert.True(await verifier.VerifyAsync(code));
        Assert.True(await verifier.VerifyAsync(code.ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public async Task Another_valid_code_is_refused()
    {
        var (verifier, _) = CreateVerifier();

        Assert.False(await verifier.VerifyAsync(RecoveryCode.Generate()));
        Assert.False(await verifier.VerifyAsync("not a code"));
    }

    [Fact]
    public async Task A_code_for_a_different_key_is_refused()
    {
        var (verifier, code) = CreateVerifier(matchingKey: false);

        Assert.False(await verifier.VerifyAsync(code));
    }

    [Fact]
    public async Task Without_a_key_file_no_code_is_accepted()
    {
        var (verifier, code) = CreateVerifier(writeFile: false);

        Assert.False(await verifier.VerifyAsync(code));
    }
}
