using WowStopwatcher.Core.Localization;

namespace WowStopwatcher.Core.Tests.Localization;

public class UkrainianTranslationTests
{
    private static Translation Ukrainian()
    {
        var files = TranslationLoader.ReadFiles();
        var english = Translation.FromJson("en", files.Single(f => f.Code == "en").Json);
        return Translation.FromJson("uk", files.Single(f => f.Code == "uk").Json, english);
    }

    [Fact]
    public void Formats_a_duration()
    {
        var uk = Ukrainian();

        Assert.Equal("1год 05хв", uk.Get("time.hoursMinutes", ("hours", 1), ("minutes", "05")));
        Assert.Equal("30с", uk.Get("time.seconds", ("seconds", 30)));
    }

    [Theory]
    [InlineData(1, "5 хв за 1 секундомір")]
    [InlineData(3, "5 хв за 3 секундоміри")]
    [InlineData(5, "5 хв за 5 секундомірів")]
    [InlineData(21, "5 хв за 21 секундомір")]
    public void Picks_the_plural_form(int count, string expected)
    {
        var uk = Ukrainian();

        Assert.Equal(expected, uk.GetPlural("today.totalAcross", count, ("time", "5 хв")));
    }

    [Fact]
    public void Formats_a_long_day_with_ukrainian_month_names()
    {
        var uk = Ukrainian();
        var text = new DateTime(2026, 3, 9).ToString(uk.Get("format.dayLong"), uk.Culture);

        Assert.Contains("березня", text);
    }
}
