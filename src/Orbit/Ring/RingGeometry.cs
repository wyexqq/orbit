using System.Windows;
using System.Windows.Media;

namespace Orbit.Ring;

internal readonly record struct SectorArc(double Start, double End)
{
    public double Middle => Start + Span / 2;

    public double Span => End - Start;
}

internal readonly record struct RingSize(double Inner, double Outer, double ExtraInner, double ExtraOuter)
{
    private const double ProgressInset = 6;

    public static RingSize Standard => AroundHole(RingGeometry.SmallestHole);

    public double ProgressRadius => Inner - ProgressInset;

    public static RingSize AroundHole(double holeRadius)
    {
        var inner = RingGeometry.ClampBetween(holeRadius, RingGeometry.SmallestHole, RingGeometry.LargestHole);
        var outer = inner + RingGeometry.BandThickness;
        var extraInner = outer + RingGeometry.ExtraGap;
        var extraOuter = extraInner + RingGeometry.ExtraThickness;
        return new RingSize(inner, outer, extraInner, extraOuter);
    }
}

internal static class RingGeometry
{
    public const double SmallestHole = 42;
    public const double LargestHole = 60;
    public const double HolePadding = 3;
    public const double BandThickness = 62;
    public const double ExtraGap = 8;
    public const double ExtraThickness = 44;
    public const double Gap = 4;
    public const double Corner = 8;
    public const double HoverShift = 5;
    public const double Slack = 44;
    public const double WindowSize = 480;
    public const double WindowHalf = WindowSize / 2;
    public const double FullTurn = Math.PI * 2;

    public static Point Polar(double radius, double angle)
    {
        return new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }

    public static double NormalizeAngle(double angle)
    {
        return ((angle % FullTurn) + FullTurn) % FullTurn;
    }

    public static double AngleNear(double angle, double reference)
    {
        var shifted = angle;
        while (shifted < reference - Math.PI)
        {
            shifted += FullTurn;
        }
        while (shifted > reference + Math.PI)
        {
            shifted -= FullTurn;
        }
        return shifted;
    }

    public static IReadOnlyList<SectorArc> SectorsFrom(double firstStart, double span, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new SectorArc(firstStart + index * span, firstStart + (index + 1) * span))
            .ToList();
    }

    public static int SectorIndexAt(double angle, IReadOnlyList<SectorArc> layout)
    {
        for (var index = 0; index < layout.Count; index++)
        {
            var sector = layout[index];
            if (NormalizeAngle(angle - sector.Start) < sector.Span)
            {
                return index;
            }
        }
        return -1;
    }

    public static double ClampBetween(double value, double min, double max)
    {
        return min > max ? (min + max) / 2 : Math.Min(Math.Max(value, min), max);
    }

    public static Geometry AnnularSector(double innerRadius, double outerRadius, SectorArc arc)
    {
        var inset = Corner / 2;
        var inner = innerRadius + inset;
        var outer = outerRadius - inset;
        var halfGap = (Gap + Corner) / 2;
        var outerStart = arc.Start + halfGap / outer;
        var outerEnd = arc.End - halfGap / outer;
        var innerStart = arc.Start + halfGap / inner;
        var innerEnd = arc.End - halfGap / inner;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(outer, outerStart), isFilled: true, isClosed: true);
            context.ArcTo(Polar(outer, outerEnd), new Size(outer, outer), 0, outerEnd - outerStart > Math.PI, SweepDirection.Clockwise, true, true);
            context.LineTo(Polar(inner, innerEnd), true, true);
            context.ArcTo(Polar(inner, innerStart), new Size(inner, inner), 0, innerEnd - innerStart > Math.PI, SweepDirection.Counterclockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }

    public static Geometry ProgressArc(double fraction, double radius)
    {
        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped >= 0.999)
        {
            var circle = new EllipseGeometry(new Point(0, 0), radius, radius);
            circle.Freeze();
            return circle;
        }
        var start = -Math.PI / 2;
        var end = start + FullTurn * clamped;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(radius, start), isFilled: false, isClosed: false);
            context.ArcTo(Polar(radius, end), new Size(radius, radius), 0, clamped > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
