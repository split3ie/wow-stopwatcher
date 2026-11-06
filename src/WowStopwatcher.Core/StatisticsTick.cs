namespace WowStopwatcher.Core;

/// <summary>A label on the shared statistics scale. <see cref="Position"/> is a fraction (0..1).</summary>
public sealed record StatisticsTick(double Position, string Label);
