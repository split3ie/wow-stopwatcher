namespace WowStopwatcher.Core.Tests;

public class TemporaryStopwatchExpiryTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Monday = new(2026, 3, 9, 15, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    private static StopwatchDefinition Def(string id = "a", bool permanent = false, bool deleted = false, DateTimeOffset? active = null) =>
        new(id, id, "#111111", null, null, 0, IsDeleted: deleted, IsPermanent: permanent, ActiveSinceUtc: active);

    private static IReadOnlyList<string> Expired(
        StopwatchDefinition def, DateTimeOffset? lastStart, DateOnly today, IReadOnlySet<string>? running = null)
    {
        var starts = new Dictionary<string, DateTimeOffset>();
        if (lastStart is { } start)
        {
            starts[def.Id] = start;
        }

        return TemporaryStopwatchExpiry.FindExpired([def], starts, running ?? None, today, Zone);
    }

    [Fact]
    public void UsedMonday_VisibleMondayAndTuesday_HiddenWednesday()
    {
        var def = Def();
        Assert.Empty(Expired(def, Monday, new DateOnly(2026, 3, 9)));
        Assert.Empty(Expired(def, Monday, new DateOnly(2026, 3, 10)));
        Assert.Equal(["a"], Expired(def, Monday, new DateOnly(2026, 3, 11)));
    }

    [Fact]
    public void Permanent_NeverExpires() =>
        Assert.Empty(Expired(Def(permanent: true), Monday, new DateOnly(2027, 1, 1)));

    [Fact]
    public void Running_NeverExpires() =>
        Assert.Empty(Expired(Def(), Monday, new DateOnly(2026, 3, 20), new HashSet<string> { "a" }));

    [Fact]
    public void Deleted_IsIgnored() =>
        Assert.Empty(Expired(Def(deleted: true), Monday, new DateOnly(2026, 3, 20)));

    [Fact]
    public void ActiveSince_CountsAsUse()
    {
        var def = Def(active: Monday.AddDays(2));
        Assert.Empty(Expired(def, Monday, new DateOnly(2026, 3, 12)));
        Assert.Equal(["a"], Expired(def, Monday, new DateOnly(2026, 3, 13)));
    }

    [Fact]
    public void ActiveSinceOnly_WithoutSessions_Expires()
    {
        var def = Def(active: Monday);
        Assert.Empty(Expired(def, null, new DateOnly(2026, 3, 10)));
        Assert.Equal(["a"], Expired(def, null, new DateOnly(2026, 3, 11)));
    }

    [Fact]
    public void NoActiveSinceAndNoSessions_DoesNotExpire() =>
        Assert.Empty(Expired(Def(), null, new DateOnly(2030, 1, 1)));

    [Fact]
    public void LaterSessionBeatsOlderActiveSince()
    {
        var def = Def(active: Monday);
        Assert.Empty(Expired(def, Monday.AddDays(3), new DateOnly(2026, 3, 13)));
    }
}
