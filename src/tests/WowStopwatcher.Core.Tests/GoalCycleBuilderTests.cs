namespace WowStopwatcher.Core.Tests;

public class GoalCycleBuilderTests
{
    private const string Id = "a";
    private static readonly TimeSpan Offset = TimeSpan.FromHours(2);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test", Offset, "test", "test");

    // Monday is 2026-03-09; times are local in the +02:00 test zone.
    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 3, day, hour, minute, 0, Offset).ToUniversalTime();

    private static Session S(DateTimeOffset start, DateTimeOffset? stop, string id = Id) => new(0, id, start, stop);

    private static IReadOnlyList<GoalCycle> Build(
        DateTimeOffset anchor, TimeSpan? goal, IEnumerable<Session> sessions, DateTimeOffset now, params GoalChange[] changes) =>
        GoalCycleBuilder.Build(Id, anchor, goal, sessions, changes, [], now, Zone);

    [Fact]
    public void SessionOverMidnight_ReachedCycleDatedStartDay_ThenOpenCycle()
    {
        var cycles = Build(At(9, 20), TimeSpan.FromHours(8), [S(At(9, 20), At(10, 4))], At(10, 12));

        Assert.Equal(2, cycles.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), cycles[0].Date);
        Assert.Equal(At(10, 4), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.FromHours(8), cycles[0].Progress);
        Assert.True(cycles[0].IsReached);
        Assert.Equal(new DateOnly(2026, 3, 10), cycles[1].Date);
        Assert.Equal(At(10, 4), cycles[1].StartUtc);
        Assert.Equal(TimeSpan.Zero, cycles[1].Progress);
        Assert.False(cycles[1].IsReached);
    }

    [Fact]
    public void LongGoal_SpansSeveralDays()
    {
        var sessions = Enumerable.Range(9, 7).Select(d => S(At(d, 10), At(d, 20))).ToList(); // 70h
        sessions.Add(S(At(16, 10), At(16, 15)));

        var cycles = Build(At(9, 0), TimeSpan.FromHours(73), sessions, At(16, 16));

        Assert.Equal(2, cycles.Count);
        Assert.Equal(new DateOnly(2026, 3, 9), cycles[0].Date);
        Assert.Equal(At(16, 13), cycles[0].ReachedUtc);
        Assert.Equal(new DateOnly(2026, 3, 16), cycles[1].Date);
        Assert.Equal(TimeSpan.FromHours(2), cycles[1].Progress);
    }

    [Fact]
    public void TwoCyclesInOneSession()
    {
        var cycles = Build(At(9, 10), TimeSpan.FromHours(2), [S(At(9, 10), At(9, 15))], At(9, 15));

        Assert.Equal(3, cycles.Count);
        Assert.Equal(At(9, 12), cycles[0].ReachedUtc);
        Assert.Equal(At(9, 12), cycles[1].StartUtc);
        Assert.Equal(At(9, 14), cycles[1].ReachedUtc);
        Assert.Equal(TimeSpan.FromHours(1), cycles[2].Progress);
        Assert.Null(cycles[2].ReachedUtc);
    }

    [Fact]
    public void Overflow_CarriesIntoNextCycle()
    {
        var sessions = new[] { S(At(9, 10), At(9, 10, 30)), S(At(9, 11), At(9, 12, 15)) };

        var cycles = Build(At(9, 0), TimeSpan.FromHours(1), sessions, At(9, 13));

        Assert.Equal(2, cycles.Count);
        Assert.Equal(At(9, 11, 30), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.FromMinutes(45), cycles[1].Progress);
    }

    [Fact]
    public void TimeBeforeAnchor_IsIgnored()
    {
        var cycles = Build(At(9, 10), TimeSpan.FromHours(3), [S(At(9, 9), At(9, 11)), S(At(8, 1), At(8, 20))], At(9, 12));

        var cycle = Assert.Single(cycles);
        Assert.Equal(At(9, 10), cycle.StartUtc);
        Assert.Equal(TimeSpan.FromHours(1), cycle.Progress);
    }

    [Fact]
    public void RunningSession_MeasuredToNow()
    {
        var cycles = Build(At(9, 0), TimeSpan.FromHours(1), [S(At(9, 10), null)], At(9, 11, 30));

        Assert.Equal(2, cycles.Count);
        Assert.Equal(At(9, 11), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), cycles[1].Progress);
    }

    [Fact]
    public void GoalRaisedMidCycle_UsesGoalInEffect()
    {
        // Goal was 2h, raised to 4h at 11:00 (1h counted by then); reached 3h later.
        var change = new GoalChange(Id, At(9, 11), TimeSpan.FromHours(2));

        var cycles = Build(At(9, 10), TimeSpan.FromHours(4), [S(At(9, 10), At(9, 15))], At(9, 15), change);

        Assert.Equal(2, cycles.Count);
        Assert.Equal(At(9, 14), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.FromHours(4), cycles[0].Goal);
        Assert.Equal(TimeSpan.FromHours(1), cycles[1].Progress);
    }

    [Fact]
    public void GoalLoweredBelowProgress_ReachedAtChange_ExcessCarried()
    {
        // Goal was 4h, lowered to 90m at 12:00 when 2h were counted.
        var change = new GoalChange(Id, At(9, 12), TimeSpan.FromHours(4));

        var cycles = Build(At(9, 10), TimeSpan.FromMinutes(90), [S(At(9, 10), At(9, 12, 30))], At(9, 13), change);

        Assert.Equal(2, cycles.Count);
        Assert.Equal(At(9, 12), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.FromMinutes(90), cycles[0].Goal);
        Assert.Equal(TimeSpan.FromMinutes(90), cycles[0].Progress);
        Assert.Equal(At(9, 12), cycles[1].StartUtc);
        Assert.Equal(TimeSpan.FromMinutes(60), cycles[1].Progress);
    }

    [Fact]
    public void GoalLowered_ExcessCompletesSeveralCyclesAtChange()
    {
        var change = new GoalChange(Id, At(9, 12), TimeSpan.FromHours(4));

        var cycles = Build(At(9, 10), TimeSpan.FromHours(1), [S(At(9, 10), At(9, 12, 30))], At(9, 13), change);

        Assert.Equal(3, cycles.Count);
        Assert.Equal(At(9, 12), cycles[0].ReachedUtc);
        Assert.Equal(At(9, 12), cycles[1].ReachedUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), cycles[2].Progress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void NoGoal_ReturnsEmpty(int? hours)
    {
        TimeSpan? goal = hours is null ? null : TimeSpan.FromHours(hours.Value);

        Assert.Empty(Build(At(9, 0), goal, [S(At(9, 10), At(9, 12))], At(9, 13)));
    }

    [Fact]
    public void AnchorAfterNow_ReturnsEmpty() =>
        Assert.Empty(Build(At(9, 14), TimeSpan.FromHours(1), [], At(9, 13)));

    [Fact]
    public void SessionsOfOtherStopwatches_AreIgnored()
    {
        var cycles = Build(At(9, 0), TimeSpan.FromHours(1), [S(At(9, 10), At(9, 12), "other"), S(At(9, 10), At(9, 10, 20))], At(9, 13));

        var cycle = Assert.Single(cycles);
        Assert.Equal(TimeSpan.FromMinutes(20), cycle.Progress);
    }

    [Fact]
    public void OverlappingSessions_CountOnce()
    {
        // Union of 10-12 and 11-13 is 3h, so a 3h goal is reached at 13:00 exactly.
        var cycles = Build(At(9, 0), TimeSpan.FromHours(3), [S(At(9, 10), At(9, 12)), S(At(9, 11), At(9, 13))], At(9, 14));

        Assert.Equal(2, cycles.Count);
        Assert.Equal(At(9, 13), cycles[0].ReachedUtc);
        Assert.Equal(TimeSpan.Zero, cycles[1].Progress);
    }

    private static IReadOnlyList<GoalCycle> BuildWithResets(
        DateTimeOffset anchor, TimeSpan? goal, IEnumerable<Session> sessions, DateTimeOffset now, GoalReset[] resets, params GoalChange[] changes) =>
        GoalCycleBuilder.Build(Id, anchor, goal, sessions, changes, resets, now, Zone);

    private static GoalReset R(DateTimeOffset at, string id = Id) => new(id, at);

    [Fact]
    public void Reset_MidCycle_CancelsWithProgress_AndStartsNewCycle()
    {
        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(8), [S(At(9, 10), At(9, 13))], At(9, 20), [R(At(9, 15))]);

        Assert.Equal(2, cycles.Count);
        Assert.True(cycles[0].IsCancelled);
        Assert.False(cycles[0].IsReached);
        Assert.Null(cycles[0].ReachedUtc);
        Assert.Equal(At(9, 15), cycles[0].CancelledUtc);
        Assert.Equal(TimeSpan.FromHours(3), cycles[0].Progress);
        Assert.Equal(TimeSpan.FromHours(8), cycles[0].Goal);
        Assert.Equal(At(9, 0), cycles[0].StartUtc);
        Assert.Equal(At(9, 15), cycles[1].StartUtc);
        Assert.Equal(TimeSpan.Zero, cycles[1].Progress);
        Assert.False(cycles[1].IsCancelled);
    }

    [Fact]
    public void Reset_DuringRunningSession_SplitsTheInterval()
    {
        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(8), [S(At(9, 10), null)], At(9, 19), [R(At(9, 12))]);

        Assert.Equal(2, cycles.Count);
        Assert.Equal(TimeSpan.FromHours(2), cycles[0].Progress);
        Assert.True(cycles[0].IsCancelled);
        Assert.Equal(TimeSpan.FromHours(7), cycles[1].Progress);
        Assert.Null(cycles[1].ReachedUtc);
    }

    [Fact]
    public void Reset_SessionSpanningReset_CountsEachPartInItsCycle()
    {
        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(5), [S(At(9, 10), At(9, 16))], At(9, 20), [R(At(9, 12))]);

        Assert.Equal(2, cycles.Count);
        Assert.Equal(TimeSpan.FromHours(2), cycles[0].Progress);
        Assert.True(cycles[0].IsCancelled);
        Assert.Equal(TimeSpan.FromHours(4), cycles[1].Progress);
    }

    [Fact]
    public void Reset_NotAfterAnchor_OrAfterNow_OrOfOtherStopwatch_IsIgnored()
    {
        var sessions = new[] { S(At(9, 10), At(9, 12)) };

        var cycles = BuildWithResets(
            At(9, 0), TimeSpan.FromHours(8), sessions, At(9, 14), [R(At(9, 0)), R(At(8, 12)), R(At(9, 15)), R(At(9, 11), "other")]);

        var only = Assert.Single(cycles);
        Assert.False(only.IsCancelled);
        Assert.Equal(TimeSpan.FromHours(2), only.Progress);
    }

    [Fact]
    public void Reset_ExactlyWhenReached_StaysReached_WithoutEmptyCancelledCycle()
    {
        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(2), [S(At(9, 10), At(9, 13))], At(9, 14), [R(At(9, 12))]);

        Assert.Equal(2, cycles.Count);
        Assert.True(cycles[0].IsReached);
        Assert.False(cycles[0].IsCancelled);
        Assert.Equal(At(9, 12), cycles[0].ReachedUtc);
        Assert.Equal(At(9, 12), cycles[1].StartUtc);
        Assert.Equal(TimeSpan.FromHours(1), cycles[1].Progress);
    }

    [Fact]
    public void Reset_WithZeroProgress_StillCancelsOpenCycle()
    {
        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(8), [], At(9, 14), [R(At(9, 12))]);

        Assert.Equal(2, cycles.Count);
        Assert.True(cycles[0].IsCancelled);
        Assert.Equal(TimeSpan.Zero, cycles[0].Progress);
        Assert.Equal(At(9, 12), cycles[1].StartUtc);
    }

    [Fact]
    public void Reset_GoalChangeBeforeReset_CancelledCycleKeepsGoalInEffect()
    {
        var change = new GoalChange(Id, At(9, 11), TimeSpan.FromHours(8));

        var cycles = BuildWithResets(At(9, 0), TimeSpan.FromHours(5), [S(At(9, 10), At(9, 13))], At(9, 20), [R(At(9, 15))], change);

        Assert.Equal(2, cycles.Count);
        Assert.True(cycles[0].IsCancelled);
        Assert.Equal(TimeSpan.FromHours(5), cycles[0].Goal);
        Assert.Equal(TimeSpan.FromHours(3), cycles[0].Progress);
        Assert.Equal(TimeSpan.Zero, cycles[1].Progress);
    }

    [Fact]
    public void Reset_Multiple_ProducesCancelledCyclesInOrder()
    {
        var cycles = BuildWithResets(
            At(9, 0), TimeSpan.FromHours(8), [S(At(9, 1), At(9, 3)), S(At(9, 5), At(9, 6))], At(9, 20), [R(At(9, 4)), R(At(9, 7))]);

        Assert.Equal(3, cycles.Count);
        Assert.Equal(TimeSpan.FromHours(2), cycles[0].Progress);
        Assert.Equal(At(9, 4), cycles[0].CancelledUtc);
        Assert.Equal(TimeSpan.FromHours(1), cycles[1].Progress);
        Assert.Equal(At(9, 4), cycles[1].StartUtc);
        Assert.Equal(At(9, 7), cycles[1].CancelledUtc);
        Assert.Equal(At(9, 7), cycles[2].StartUtc);
        Assert.False(cycles[2].IsCancelled);
    }
}
