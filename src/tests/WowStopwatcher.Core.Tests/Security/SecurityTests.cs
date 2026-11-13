using WowStopwatcher.Core.Security;

namespace WowStopwatcher.Core.Tests.Security;

public sealed class SecurityTests
{
    [Fact]
    public void DatabaseKey_GeneratesDistinct32ByteKeysAndRoundTripsHex()
    {
        var a = DatabaseKey.Generate();
        var b = DatabaseKey.Generate();

        Assert.Equal(32, a.ToBytes().Length);
        Assert.False(a.KeyEquals(b));
        Assert.Equal(64, a.ToHex().Length);
        Assert.True(a.KeyEquals(DatabaseKey.FromHex(a.ToHex())));
        Assert.Null(DatabaseKey.FromHex("zz"));
        Assert.Null(DatabaseKey.FromHex(new string('g', 64)));
        Assert.Throws<ArgumentException>(() => DatabaseKey.FromBytes(new byte[5]));
    }

    [Fact]
    public void RecoveryCode_GeneratesValidGroupedCodesWithEnoughEntropy()
    {
        var code = RecoveryCode.Generate();

        Assert.Equal(29, code.Length);
        Assert.Equal(5, code.Count(c => c == '-'));
        Assert.True(RecoveryCode.IsValid(code));
        Assert.True(RecoveryCode.Length * 5 >= 100);
        Assert.NotEqual(code, RecoveryCode.Generate());
    }

    [Fact]
    public void RecoveryCode_NormalizeIgnoresCaseSpacesDashesAndLookAlikes()
    {
        Assert.Equal("0123456789ABCDEFGHJKMNPQ", RecoveryCode.Normalize(" 0123-4567 89ab-cdef ghjk-mnpq "));
        Assert.Equal("0011", RecoveryCode.Normalize("OoIl"));
        Assert.Equal(string.Empty, RecoveryCode.Normalize(null));
    }

    [Fact]
    public void RecoveryCode_IsValidRejectsWrongLengthAndCharacters()
    {
        var code = RecoveryCode.Generate();

        Assert.True(RecoveryCode.IsValid(code.ToLowerInvariant().Replace("-", " ")));
        Assert.False(RecoveryCode.IsValid(code[..^1]));
        Assert.False(RecoveryCode.IsValid(code + "A"));
        Assert.False(RecoveryCode.IsValid(code[..^1] + "U"));
        Assert.False(RecoveryCode.IsValid(null));
    }

    [Fact]
    public void Wrap_ThenUnwrapWithSameCode_ReturnsKey()
    {
        var key = DatabaseKey.Generate();
        var code = RecoveryCode.Generate();

        var wrapped = DatabaseKeyWrapper.Wrap(key, code);

        Assert.Equal(DatabaseKeyWrapper.DefaultIterations, wrapped.Iterations);
        Assert.Equal(16, wrapped.Salt.Length);
        Assert.Equal(12, wrapped.Nonce.Length);
        Assert.Equal(16, wrapped.Tag.Length);
        Assert.Equal(32, wrapped.Ciphertext.Length);
        Assert.True(key.KeyEquals(DatabaseKeyWrapper.TryUnwrap(wrapped, code.ToLowerInvariant().Replace("-", ""))));
    }

    [Fact]
    public void Unwrap_WithWrongCodeOrTamperedData_ReturnsNull()
    {
        var key = DatabaseKey.Generate();
        var code = RecoveryCode.Generate();
        var wrapped = DatabaseKeyWrapper.Wrap(key, code, iterations: 1000);

        Assert.Null(DatabaseKeyWrapper.TryUnwrap(wrapped, RecoveryCode.Generate()));

        var tampered = wrapped.Ciphertext.ToArray();
        tampered[0] ^= 1;
        Assert.Null(DatabaseKeyWrapper.TryUnwrap(wrapped with { Ciphertext = tampered }, code));
        Assert.Null(DatabaseKeyWrapper.TryUnwrap(wrapped with { Kdf = "other" }, code));
        Assert.Null(DatabaseKeyWrapper.TryUnwrap(wrapped with { Iterations = int.MaxValue }, code));
        Assert.Null(DatabaseKeyWrapper.TryUnwrap(wrapped with { Salt = new byte[3] }, code));
    }

    [Fact]
    public void Wrap_UsesFreshSaltAndNonceEachTime()
    {
        var key = DatabaseKey.Generate();
        var code = RecoveryCode.Generate();

        var first = DatabaseKeyWrapper.Wrap(key, code, 1000);
        var second = DatabaseKeyWrapper.Wrap(key, code, 1000);

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
    }
}
