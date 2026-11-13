using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Core.Tests.Security;

public sealed class BackupEnvelopeTests
{
    private const int Fast = 1000;

    [Fact]
    public void RoundTrip_ReturnsPayload_AndStartsWithMagicAndVersion()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var envelope = BackupEnvelope.Encrypt(payload, "pw-with-ü", Fast);

        Assert.True(BackupEnvelope.HasMagic(envelope));
        Assert.Equal((byte)'W', envelope[0]);
        Assert.Equal(BackupEnvelope.CurrentVersion, envelope[4]);
        Assert.Equal(BackupDecryptStatus.Success, BackupEnvelope.TryDecrypt(envelope, "pw-with-ü", out var plain));
        Assert.Equal(payload, plain);
    }

    [Fact]
    public void EmptyPayload_RoundTrips()
    {
        var envelope = BackupEnvelope.Encrypt([], "pw", Fast);

        Assert.Equal(BackupDecryptStatus.Success, BackupEnvelope.TryDecrypt(envelope, "pw", out var plain));
        Assert.Empty(plain!);
    }

    [Fact]
    public void WrongOrEmptyPassword_IsRejected()
    {
        var envelope = BackupEnvelope.Encrypt([9, 9], "right", Fast);

        Assert.Equal(BackupDecryptStatus.WrongPasswordOrTampered, BackupEnvelope.TryDecrypt(envelope, "wrong", out var plain));
        Assert.Null(plain);
        Assert.Equal(BackupDecryptStatus.WrongPasswordOrTampered, BackupEnvelope.TryDecrypt(envelope, "", out _));
    }

    [Fact]
    public void Encrypt_RejectsEmptyPassword()
    {
        Assert.Throws<ArgumentException>(() => BackupEnvelope.Encrypt([1], "", Fast));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(40)]
    public void TamperedByte_IsNeverAccepted(int index)
    {
        var envelope = BackupEnvelope.Encrypt(new byte[64], "pw", Fast);
        envelope[index] ^= 0x01;

        Assert.NotEqual(BackupDecryptStatus.Success, BackupEnvelope.TryDecrypt(envelope, "pw", out var plain));
        Assert.Null(plain);
    }

    [Fact]
    public void TamperedTag_IsNotAccepted()
    {
        var envelope = BackupEnvelope.Encrypt(new byte[8], "pw", Fast);
        envelope[^1] ^= 0xFF;

        Assert.Equal(BackupDecryptStatus.WrongPasswordOrTampered, BackupEnvelope.TryDecrypt(envelope, "pw", out _));
    }

    [Fact]
    public void TruncatedOrForeignData_IsDamaged()
    {
        var envelope = BackupEnvelope.Encrypt(new byte[8], "pw", Fast);

        Assert.Equal(BackupDecryptStatus.Damaged, BackupEnvelope.TryDecrypt(envelope.AsSpan(0, 20), "pw", out _));
        Assert.Equal(BackupDecryptStatus.Damaged, BackupEnvelope.TryDecrypt(new byte[64], "pw", out _));
        Assert.False(BackupEnvelope.HasMagic("PK"u8));
    }

    [Fact]
    public void TwoEncryptions_DifferEvenForTheSamePayloadAndPassword()
    {
        var a = BackupEnvelope.Encrypt([1, 2, 3], "pw", Fast);
        var b = BackupEnvelope.Encrypt([1, 2, 3], "pw", Fast);

        Assert.NotEqual(a, b);
    }
}
