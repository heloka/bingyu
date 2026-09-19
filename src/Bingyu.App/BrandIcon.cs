using System.Windows.Shapes;

namespace Bingyu;

internal static class BrandIcon
{
    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // The title bar uses vector geometry so high-DPI displays never scale up an ICO frame.
    public static FrameworkElement Create(double size)
    {
        var canvas = new Canvas { Width = 256, Height = 256, SnapsToDevicePixels = true };
        var tile = new Rectangle
        {
            Width = 248, Height = 248, RadiusX = 58, RadiusY = 58,
            Fill = new LinearGradientBrush(ColorOf("#101933"), ColorOf("#35205E"), 135),
            Stroke = new SolidColorBrush(Color.FromArgb(68, 185, 167, 255)), StrokeThickness = 2
        };
        Canvas.SetLeft(tile, 4); Canvas.SetTop(tile, 4); canvas.Children.Add(tile);

        var orbit = new Ellipse
        {
            Width = 138, Height = 138,
            Stroke = new LinearGradientBrush(ColorOf("#5DE8F2"), ColorOf("#A66BFF"), 55),
            StrokeThickness = 23
        };
        Canvas.SetLeft(orbit, 58); Canvas.SetTop(orbit, 58); canvas.Children.Add(orbit);

        var tail = new Line
        {
            X1 = 170, Y1 = 170, X2 = 203, Y2 = 203,
            Stroke = new SolidColorBrush(ColorOf("#A66BFF")), StrokeThickness = 23,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        canvas.Children.Add(tail);

        var core = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M127,88 L139,116 L167,128 L139,140 L127,168 L115,140 L87,128 L115,116 Z"),
            Fill = new SolidColorBrush(ColorOf("#F4FBFF"))
        };
        canvas.Children.Add(core);
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, SnapsToDevicePixels = true };
    }
}
