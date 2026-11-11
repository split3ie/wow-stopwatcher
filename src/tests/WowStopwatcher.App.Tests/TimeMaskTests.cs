using Avalonia.Input;
using WowStopwatcher.App.Controls;

namespace WowStopwatcher.App.Tests;

public sealed class TimeMaskTests
{
    private const string Duration3 = "000:00";
    private const string Clock = "00:00";

    [Fact]
    public void Digit_overtypes_and_moves_caret()
    {
        Assert.True(TimeMask.TryApply(Duration3, "160:00", 0, Key.D2, out var text, out var caret));
        Assert.Equal("260:00", text);
        Assert.Equal(1, caret);
    }

    [Fact]
    public void Digit_before_colon_skips_it()
    {
        Assert.True(TimeMask.TryApply(Duration3, "000:00", 2, Key.D7, out var text, out var caret));
        Assert.Equal("007:00", text);
        Assert.Equal(4, caret);
    }

    [Fact]
    public void Numpad_digit_works()
    {
        Assert.True(TimeMask.TryApply(Clock, "00:00", 0, Key.NumPad1, out var text, out _));
        Assert.Equal("10:00", text);
    }

    [Fact]
    public void Digit_at_end_is_ignored()
    {
        Assert.False(TimeMask.TryApply(Clock, "10:30", 5, Key.D1, out _, out _));
    }

    [Fact]
    public void Backspace_zeroes_previous_digit_skipping_colon()
    {
        Assert.True(TimeMask.TryApply(Duration3, "123:45", 4, Key.Back, out var text, out var caret));
        Assert.Equal("120:45", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void Backspace_at_start_does_nothing() =>
        Assert.False(TimeMask.TryApply(Duration3, "123:45", 0, Key.Back, out _, out _));

    [Fact]
    public void Delete_zeroes_digit_at_caret_skipping_colon()
    {
        Assert.True(TimeMask.TryApply(Duration3, "123:45", 3, Key.Delete, out var text, out var caret));
        Assert.Equal("123:05", text);
        Assert.Equal(4, caret);
    }

    [Fact]
    public void Minute_tens_above_five_is_rejected()
    {
        Assert.False(TimeMask.TryApply(Duration3, "000:00", 4, Key.D6, out _, out _));
        Assert.True(TimeMask.TryApply(Duration3, "000:00", 4, Key.D5, out var text, out _));
        Assert.Equal("000:50", text);
    }

    [Fact]
    public void Clock_hours_above_23_are_rejected()
    {
        Assert.False(TimeMask.TryApply(Clock, "00:00", 0, Key.D3, out _, out _, maxHours: 23));
        Assert.False(TimeMask.TryApply(Clock, "04:00", 0, Key.D2, out _, out _, maxHours: 23));
        Assert.True(TimeMask.TryApply(Clock, "00:00", 1, Key.D3, out var text, out _, maxHours: 23));
        Assert.Equal("03:00", text);
    }

    [Fact]
    public void Duration_hours_up_to_99_are_allowed_without_a_limit()
    {
        Assert.True(TimeMask.TryApply(Clock, "00:00", 0, Key.D9, out var text, out _));
        Assert.Equal("90:00", text);
    }

    [Theory]
    [InlineData(Key.A)]
    [InlineData(Key.Space)]
    [InlineData(Key.OemPeriod)]
    [InlineData(Key.Enter)]
    public void Non_digit_keys_are_rejected(Key key) =>
        Assert.False(TimeMask.TryApply(Duration3, "000:00", 0, key, out _, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1:30")]
    [InlineData("abc:de")]
    public void Text_not_matching_the_shape_starts_from_zeros(string? start)
    {
        Assert.True(TimeMask.TryApply(Duration3, start, 0, Key.D1, out var text, out var caret));
        Assert.Equal("100:00", text);
        Assert.Equal(1, caret);
    }
}
