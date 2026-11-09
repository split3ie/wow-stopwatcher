namespace WowStopwatcher.Core;

/// <summary>
/// Build-time switches for optional stopwatch capabilities. A capability that is off makes the app see stopwatch
/// definitions as if it did not exist; the stored values are kept (see <see cref="CapabilityFilteredStopwatchRepository"/>).
/// </summary>
/// <param name="Independent">Off: every stopwatch is treated as normal (<c>IsIndependent = false</c>).</param>
/// <param name="Goals">Off: no stopwatch has a goal (<c>Goal = null</c>).</param>
/// <param name="Permanent">Off: every stopwatch is treated as temporary (<c>IsPermanent = false</c>),
/// so the temporary-expiry rules apply to all of them.</param>
public sealed record StopwatchCapabilities(bool Independent, bool Goals, bool Permanent)
{
    /// <summary>All capabilities on.</summary>
    public static StopwatchCapabilities All { get; } = new(true, true, true);
}
