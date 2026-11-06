using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace WowStopwatcher.App.Controls;

/// <summary>
/// Draws a geometry authored on a ViewWidth x ViewHeight grid (24 x 24 by default) scaled to the control size,
/// like an inline SVG. <see cref="Foreground"/> inherits so icons can follow the text color ("currentColor").
/// </summary>
public class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.8);

    public static readonly StyledProperty<double> ViewWidthProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(ViewWidth), 24);

    public static readonly StyledProperty<double> ViewHeightProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(ViewHeight), 24);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    static Icon()
    {
        AffectsRender<Icon>(
            DataProperty, FillProperty, StrokeProperty, StrokeThicknessProperty,
            ViewWidthProperty, ViewHeightProperty, ForegroundProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double ViewWidth
    {
        get => GetValue(ViewWidthProperty);
        set => SetValue(ViewWidthProperty, value);
    }

    public double ViewHeight
    {
        get => GetValue(ViewHeightProperty);
        set => SetValue(ViewHeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Data is not { } data || ViewWidth <= 0 || ViewHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / ViewWidth, Bounds.Height / ViewHeight);
        var pen = Stroke is { } stroke && StrokeThickness > 0
            ? new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : null;

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawGeometry(Fill, pen, data);
        }
    }
}
