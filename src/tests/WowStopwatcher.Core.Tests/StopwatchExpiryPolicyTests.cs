using Microsoft.Extensions.Time.Testing;

namespace WowStopwatcher.Core.Tests;

public class StopwatchExpiryPolicyTests
{
    private sealed class HideEverythingPolicy : IStopwatchExpiryPolicy
    {
        public IReadOnlyList<string> FindExpired(
            IEnumerable<StopwatchDefinition> definitions,
            IReadOnlyDictionary<string, DateTimeOffset> latestStartPerStopwatch,
            IReadOnlySet<string> runningIds,
            DateOnly today,
            TimeZoneInfo zone) =>
            definitions.Where(d => !d.IsDeleted).Select(d => d.Id).ToList();
    }

    [Fact]
    public void DefaultPolicy_DelegatesToTemporaryStopwatchExpiry()
    {
        var monday = new DateTimeOffset(2026, 3, 9, 15, 0, 0, TimeSpan.Zero);
        var def = new StopwatchDefinition("a", "a", "#111111", null, null, 0, ActiveSinceUtc: monday);
        var starts = new Dictionary<string, DateTimeOffset>();
        var none = new HashSet<string>();

        Assert.Empty(new TemporaryStopwatchExpiryPolicy().FindExpired([def], starts, none, new DateOnly(2026, 3, 10), TimeZoneInfo.Utc));
        Assert.Equal(["a"], new TemporaryStopwatchExpiryPolicy().FindExpired([def], starts, none, new DateOnly(2026, 3, 11), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Engine_UsesInjectedPolicy()
    {
        var stopwatches = new FakeStopwatchRepository();
        stopwatches.Add(new StopwatchDefinition("a", "a", "#111111", null, null, 0));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 15, 0, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);

        _ = new StopwatchEngine(stopwatches, new FakeSessionRepository(), time, new HideEverythingPolicy());

        Assert.True(stopwatches.Items.Single().IsDeleted);
    }
}
