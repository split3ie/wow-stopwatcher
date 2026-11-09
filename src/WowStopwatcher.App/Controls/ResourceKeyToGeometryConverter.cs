using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WowStopwatcher.App.Controls;

/// <summary>Looks up the geometry stored in the application resources under the given key. Gives null for an empty or unknown key.</summary>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public static readonly ResourceKeyToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key
            && Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out var resource))
        {
            return resource as Geometry;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
