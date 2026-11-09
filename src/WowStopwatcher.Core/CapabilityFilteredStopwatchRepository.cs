namespace WowStopwatcher.Core;

/// <summary>
/// Wraps a stopwatch repository and hides the fields of disabled <see cref="StopwatchCapabilities"/>:
/// reads return them in their neutral state, while updates keep the values stored in the inner repository,
/// so turning a capability back on restores them.
/// </summary>
public sealed class CapabilityFilteredStopwatchRepository(IStopwatchRepository inner, StopwatchCapabilities capabilities)
    : IStopwatchRepository
{
    /// <inheritdoc />
    public IReadOnlyList<StopwatchDefinition> GetAll(bool includeDeleted = false) =>
        inner.GetAll(includeDeleted).Select(Neutralize).ToList();

    /// <inheritdoc />
    public StopwatchDefinition? Get(string id) => inner.Get(id) is { } stored ? Neutralize(stored) : null;

    /// <inheritdoc />
    public void Add(StopwatchDefinition stopwatch) => inner.Add(Neutralize(stopwatch));

    /// <inheritdoc />
    public void Update(StopwatchDefinition stopwatch)
    {
        if (inner.Get(stopwatch.Id) is { } stored)
        {
            stopwatch = stopwatch with
            {
                IsIndependent = capabilities.Independent ? stopwatch.IsIndependent : stored.IsIndependent,
                Goal = capabilities.Goals ? stopwatch.Goal : stored.Goal,
                IsPermanent = capabilities.Permanent ? stopwatch.IsPermanent : stored.IsPermanent,
            };
        }

        inner.Update(stopwatch);
    }

    /// <inheritdoc />
    public void SoftDelete(string id) => inner.SoftDelete(id);

    /// <inheritdoc />
    public void SaveSortOrder(IReadOnlyList<string> orderedIds) => inner.SaveSortOrder(orderedIds);

    private StopwatchDefinition Neutralize(StopwatchDefinition stopwatch) => stopwatch with
    {
        IsIndependent = capabilities.Independent && stopwatch.IsIndependent,
        Goal = capabilities.Goals ? stopwatch.Goal : null,
        IsPermanent = capabilities.Permanent && stopwatch.IsPermanent,
    };
}
