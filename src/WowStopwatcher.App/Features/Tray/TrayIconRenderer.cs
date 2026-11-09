using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WowStopwatcher.App.Features.Tray;

/// <summary>Renders the stopwatch glyph for the tray, optionally in a stopwatch color with a status dot.</summary>
public static class TrayIconRenderer
{
    private const int Size = 32;

    /// <param name="running">Color of the running stopwatch, or null for the idle icon (accent color, no dot).</param>
    public static WindowIcon Render(Color? running)
    {
        var app = Application.Current!;
        var accent = (Color)app.FindResource("AccentColor")!;
        var color = running ?? accent;
        var glyph = (Geometry)app.FindResource("IconLogo")!;

        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var scale = Size / 24.0;
            var pen = new Pen(new SolidColorBrush(color), 2.2 * scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                context.DrawGeometry(null, pen, glyph);
            }

            if (running is not null)
            {
                // Dot at the bottom-right with a light ring so it stays visible on the glyph.
                var ring = (Color)app.FindResource("BorderColor")!;
                var center = new Point(Size - 7, Size - 7);
                context.DrawEllipse(new SolidColorBrush(ring), null, center, 6.5, 6.5);
                context.DrawEllipse(new SolidColorBrush(color), null, center, 4.5, 4.5);
            }
        }

        return new WindowIcon(bitmap);
    }
}
