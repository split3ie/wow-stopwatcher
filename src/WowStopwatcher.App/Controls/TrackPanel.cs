using Avalonia;
using Avalonia.Controls;

namespace WowStopwatcher.App.Controls;

/// <summary>How <see cref="TrackPanel"/> places a child along the timeline track.</summary>
public enum TrackPlacement
{
    /// <summary>Spans Start..Start+Width (fractions of the track width) over the full track height.</summary>
    Span,

    /// <summary>Centered on Start with its own width; extends upward by Rise pixels (goal pins, the "now" marker).</summary>
    Pin,

    /// <summary>Centered on Start (kept inside the track), with its bottom Rise pixels above the track (tooltips).</summary>
    Above,
}

/// <summary>
/// Lays children out along a timeline track. Positions are fractions (0..1) of the panel width,
/// so a view model can update them in place without the controls being recreated.
/// </summary>
public class TrackPanel : Panel
{
    public static readonly AttachedProperty<TrackPlacement> PlacementProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, TrackPlacement>("Placement");

    public static readonly AttachedProperty<double> StartProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, double>("Start");

    public static readonly AttachedProperty<double> SpanWidthProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, double>("SpanWidth");

    public static readonly AttachedProperty<double> RiseProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, double>("Rise");

    /// <summary>Span only: top of the child as a fraction of the track height.</summary>
    public static readonly AttachedProperty<double> SpanTopProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, double>("SpanTop", 0.0);

    /// <summary>Span only: height of the child as a fraction of the track height.</summary>
    public static readonly AttachedProperty<double> SpanHeightProperty =
        AvaloniaProperty.RegisterAttached<TrackPanel, Control, double>("SpanHeight", 1.0);

    static TrackPanel()
    {
        AffectsParentArrange<TrackPanel>(PlacementProperty, StartProperty, SpanWidthProperty, RiseProperty, SpanTopProperty, SpanHeightProperty);
    }

    public static double GetSpanTop(Control control) => control.GetValue(SpanTopProperty);

    public static void SetSpanTop(Control control, double value) => control.SetValue(SpanTopProperty, value);

    public static double GetSpanHeight(Control control) => control.GetValue(SpanHeightProperty);

    public static void SetSpanHeight(Control control, double value) => control.SetValue(SpanHeightProperty, value);

    public static TrackPlacement GetPlacement(Control control) => control.GetValue(PlacementProperty);

    public static void SetPlacement(Control control, TrackPlacement value) => control.SetValue(PlacementProperty, value);

    public static double GetStart(Control control) => control.GetValue(StartProperty);

    public static void SetStart(Control control, double value) => control.SetValue(StartProperty, value);

    public static double GetSpanWidth(Control control) => control.GetValue(SpanWidthProperty);

    public static void SetSpanWidth(Control control, double value) => control.SetValue(SpanWidthProperty, value);

    public static double GetRise(Control control) => control.GetValue(RiseProperty);

    public static void SetRise(Control control, double value) => control.SetValue(RiseProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (GetPlacement(child) == TrackPlacement.Span)
            {
                height = Math.Max(height, child.DesiredSize.Height);
            }
        }

        // The panel has no natural width: it fills what the parent gives it.
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = finalSize.Width;
        var height = finalSize.Height;
        foreach (var child in Children)
        {
            var start = GetStart(child) * width;
            var rise = GetRise(child);
            switch (GetPlacement(child))
            {
                case TrackPlacement.Span:
                    child.Arrange(new Rect(
                        start,
                        GetSpanTop(child) * height,
                        Math.Max(0, GetSpanWidth(child) * width),
                        Math.Max(0, GetSpanHeight(child) * height)));
                    break;

                case TrackPlacement.Pin:
                    var pinWidth = child.DesiredSize.Width;
                    child.Arrange(new Rect(start - pinWidth / 2, -rise, pinWidth, height + rise));
                    break;

                case TrackPlacement.Above:
                    var size = child.DesiredSize;
                    var x = Math.Clamp(start - size.Width / 2, 0, Math.Max(0, width - size.Width));
                    child.Arrange(new Rect(x, -rise - size.Height, size.Width, size.Height));
                    break;
            }
        }

        return finalSize;
    }
}
