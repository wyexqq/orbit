using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Orbit.Ui;

internal static class OrbitMark
{
    private const double DrawingSize = 48;
    private const double TileInset = 2;
    private const double TileCorner = 11;
    private const double RingCenter = 24;
    private const double RingRadius = 11.5;
    private const double RingThickness = 3.2;
    private const double PlanetX = 33.5;
    private const double PlanetY = 14.5;
    private const double PlanetRadius = 3.6;
    private const double PlanetGapRadius = 5.6;

    public static readonly Color Accent = Palette.Hex("#2F6FE0");

    public static FrameworkElement Create(double size)
    {
        var canvas = new Canvas { Width = DrawingSize, Height = DrawingSize };
        var tileSide = DrawingSize - TileInset * 2;
        var tile = new Rectangle
        {
            Width = tileSide,
            Height = tileSide,
            RadiusX = TileCorner,
            RadiusY = TileCorner,
            Fill = new SolidColorBrush(Accent),
        };
        AddAt(canvas, tile, TileInset, TileInset);

        var ringOuterRadius = RingRadius + RingThickness / 2;
        var ring = new Ellipse
        {
            Width = ringOuterRadius * 2,
            Height = ringOuterRadius * 2,
            Stroke = Brushes.White,
            StrokeThickness = RingThickness,
        };
        AddAt(canvas, ring, RingCenter - ringOuterRadius, RingCenter - ringOuterRadius);

        AddDisc(canvas, PlanetX, PlanetY, PlanetGapRadius, new SolidColorBrush(Accent));
        AddDisc(canvas, PlanetX, PlanetY, PlanetRadius, Brushes.White);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static void AddDisc(Canvas canvas, double centerX, double centerY, double radius, Brush fill)
    {
        var disc = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill };
        AddAt(canvas, disc, centerX - radius, centerY - radius);
    }

    private static void AddAt(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }
}
