using Avalonia;
using Avalonia.Controls;

namespace WowStopwatcher.App.Layout;

/// <summary>
/// A small CSS-flexbox-like wrapping layout: each child has a basis width (its preferred width) and a grow weight.
/// Children fill a line while their bases fit; the free space of a line is shared by grow weight.
/// With <see cref="ReverseLines"/> (flex-wrap: wrap-reverse) later lines are placed above earlier ones.
/// </summary>
public class FlexWrapPanel : Panel
{
    public static readonly AttachedProperty<double> BasisProperty =
        AvaloniaProperty.RegisterAttached<FlexWrapPanel, Control, double>("Basis", 260);

    public static readonly AttachedProperty<double> GrowProperty =
        AvaloniaProperty.RegisterAttached<FlexWrapPanel, Control, double>("Grow", 1);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FlexWrapPanel, double>(nameof(Spacing));

    public static readonly StyledProperty<bool> ReverseLinesProperty =
        AvaloniaProperty.Register<FlexWrapPanel, bool>(nameof(ReverseLines));

    static FlexWrapPanel()
    {
        AffectsMeasure<FlexWrapPanel>(SpacingProperty, ReverseLinesProperty);
        AffectsParentMeasure<FlexWrapPanel>(BasisProperty, GrowProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public bool ReverseLines
    {
        get => GetValue(ReverseLinesProperty);
        set => SetValue(ReverseLinesProperty, value);
    }

    public static double GetBasis(Control control) => control.GetValue(BasisProperty);

    public static void SetBasis(Control control, double value) => control.SetValue(BasisProperty, value);

    public static double GetGrow(Control control) => control.GetValue(GrowProperty);

    public static void SetGrow(Control control, double value) => control.SetValue(GrowProperty, value);

    private readonly record struct Line(int First, int Count, double Height);

    private List<Line> _lines = [];
    private double[] _widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children.Where(c => c.IsVisible).ToList();
        var width = double.IsInfinity(availableSize.Width)
            ? children.Sum(GetBasis) + Math.Max(0, children.Count - 1) * Spacing
            : availableSize.Width;

        _lines = [];
        _widths = new double[children.Count];

        var index = 0;
        var totalHeight = 0.0;
        while (index < children.Count)
        {
            var count = 1;
            var used = GetBasis(children[index]);
            while (index + count < children.Count)
            {
                var next = used + Spacing + GetBasis(children[index + count]);
                if (next > width)
                {
                    break;
                }

                used = next;
                count++;
            }

            var free = Math.Max(0, width - used);
            var growSum = 0.0;
            for (var i = 0; i < count; i++)
            {
                growSum += GetGrow(children[index + i]);
            }

            var lineHeight = 0.0;
            for (var i = 0; i < count; i++)
            {
                var child = children[index + i];
                var share = growSum > 0 ? free * GetGrow(child) / growSum : 0;
                var childWidth = Math.Min(width, GetBasis(child) + share);
                _widths[index + i] = childWidth;
                child.Measure(new Size(childWidth, double.PositiveInfinity));
                lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
            }

            _lines.Add(new Line(index, count, lineHeight));
            totalHeight += lineHeight + (_lines.Count > 1 ? Spacing : 0);
            index += count;
        }

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(c => c.IsVisible).ToList();
        var ordered = ReverseLines ? Enumerable.Reverse(_lines).ToList() : _lines;
        var y = 0.0;
        foreach (var line in ordered)
        {
            var x = 0.0;
            for (var i = 0; i < line.Count; i++)
            {
                var child = children[line.First + i];
                var width = _widths[line.First + i];
                child.Arrange(new Rect(x, y, width, line.Height));
                x += width + Spacing;
            }

            y += line.Height + Spacing;
        }

        return finalSize;
    }
}
