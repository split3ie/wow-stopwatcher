using WowStopwatcher.Core;

namespace WowStopwatcher.Core.Tests;

public sealed class FakeStopwatchRepository : IStopwatchRepository
{
    public List<StopwatchDefinition> Items { get; } = [];

    public IReadOnlyList<StopwatchDefinition> GetAll(bool includeDeleted = false) =>
        Items.Where(s => includeDeleted || !s.IsDeleted).OrderBy(s => s.SortOrder).ToList();

    public StopwatchDefinition? Get(string id) => Items.FirstOrDefault(s => s.Id == id);

    public void Add(StopwatchDefinition stopwatch) => Items.Add(stopwatch);

    public void Update(StopwatchDefinition stopwatch) => Items[Items.FindIndex(s => s.Id == stopwatch.Id)] = stopwatch;

    public void SoftDelete(string id) => Update(Items.First(s => s.Id == id) with { IsDeleted = true });

    public void SaveSortOrder(IReadOnlyList<string> orderedIds)
    {
        for (var i = 0; i < orderedIds.Count; i++)
        {
            Update(Items.First(s => s.Id == orderedIds[i]) with { SortOrder = i });
        }
    }
}

public sealed class FakeSessionRepository : ISessionRepository
{
    private long _nextId = 1;

    public List<Session> Items { get; } = [];

    public long InsertOpen(string stopwatchId, DateTimeOffset startUtc)
    {
        var id = _nextId++;
        Items.Add(new Session(id, stopwatchId, startUtc, null));
        return id;
    }

    public void Close(long sessionId, DateTimeOffset stopUtc)
    {
        var index = Items.FindIndex(s => s.Id == sessionId);
        Items[index] = Items[index] with { StopUtc = stopUtc };
    }

    public IReadOnlyList<Session> GetOpen() => Items.Where(s => s.IsOpen).OrderBy(s => s.StartUtc).ToList();

    public Session? GetLatest() => Items.OrderByDescending(s => s.StartUtc).ThenByDescending(s => s.Id).FirstOrDefault();

    public Session? GetLatestForStopwatches(IReadOnlyCollection<string> stopwatchIds) =>
        Items.Where(s => stopwatchIds.Contains(s.StopwatchId))
            .OrderByDescending(s => s.StartUtc).ThenByDescending(s => s.Id).FirstOrDefault();

    public IReadOnlyList<Session> GetInRange(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Items.Where(s => s.StartUtc >= fromUtc && s.StartUtc < toUtc).OrderBy(s => s.StartUtc).ThenBy(s => s.Id).ToList();

    public IReadOnlyList<DateOnly> GetLocalDays(TimeZoneInfo zone) =>
        Items.Select(s => LocalDays.ToLocalDate(s.StartUtc, zone)).Distinct().OrderByDescending(d => d).ToList();

    public int DeleteForStopwatchInRange(string stopwatchId, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        Items.RemoveAll(s => s.StopwatchId == stopwatchId && s.StartUtc >= fromUtc && s.StartUtc < toUtc);

    public int DeleteForLocalDays(IEnumerable<DateOnly> days, TimeZoneInfo zone)
    {
        var set = days.ToHashSet();
        return Items.RemoveAll(s => set.Contains(LocalDays.ToLocalDate(s.StartUtc, zone)));
    }

    public int DeleteAll()
    {
        var count = Items.Count;
        Items.Clear();
        return count;
    }
}
