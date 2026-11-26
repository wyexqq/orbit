using System.Windows;
using System.Windows.Media;

namespace Orbit.Ring;

internal readonly record struct SectorArc(double Start, double End)
{
    public double Middle => Start + Span / 2;

    public double Span => End - Start;
}

internal readonly record struct RingExtents(double Left, double Right, double Top, double Bottom);

internal static class RingGeometry
{
    public const double InnerRadius = 42;
    public const double OuterRadius = 104;
    public const double ExtraInnerRadius = 112;
    public const double ExtraOuterRadius = 156;
    public const double CenterRadius = 33;
    public const double ProgressRadius = 38;
    public const double Gap = 4;
    public const double Corner = 8;
    public const double HoverShift = 5;
    public const double Slack = 44;
    public const double WindowSize = 480;
    public const double WindowHalf = WindowSize / 2;
    public const int MainSlots = 5;
    public const int MaxWithoutMore = 6;

    private const double FullTurn = Math.PI * 2;
    private const double ExtraMaxSpan = FullTurn / 9;
    private const double ExtraTotalLimit = Math.PI * 5 / 3;

    public static Point Polar(double radius, double angle)
    {
        return new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
    }

    public static double NormalizeAngle(double angle)
    {
        return ((angle % FullTurn) + FullTurn) % FullTurn;
    }

    public static IReadOnlyList<SectorArc> SectorsFrom(double firstStart, double span, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new SectorArc(firstStart + index * span, firstStart + (index + 1) * span))
            .ToList();
    }

    public static IReadOnlyList<SectorArc> LayoutMain(int count)
    {
        if (count == 0)
        {
            return [];
        }
        var span = FullTurn / count;
        return SectorsFrom(-Math.PI / 2 - span / 2, span, count);
    }

    public static IReadOnlyList<SectorArc> LayoutExtras(SectorArc moreSector, int count)
    {
        var preferredSpan = Math.Min(ExtraMaxSpan, ExtraTotalLimit / count);
        var span = Math.Max(preferredSpan, moreSector.Span / count);
        return SectorsFrom(moreSector.Middle - span * count / 2, span, count);
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

    public static RingExtents Extents(IReadOnlyList<SectorArc> extraLayout)
    {
        var mainReach = OuterRadius + 10;
        double left = mainReach, right = mainReach, top = mainReach, bottom = mainReach;
        const int samplesPerSector = 8;
        foreach (var sector in extraLayout)
        {
            for (var step = 0; step <= samplesPerSector; step++)
            {
                var angle = sector.Start + sector.Span * step / samplesPerSector;
                var point = Polar(ExtraOuterRadius + 6, angle);
                left = Math.Max(left, -point.X);
                right = Math.Max(right, point.X);
                top = Math.Max(top, -point.Y);
                bottom = Math.Max(bottom, point.Y);
            }
        }
        return new RingExtents(left, right, top, bottom);
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

    public static Geometry ProgressArc(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0, 1);
        if (clamped >= 0.999)
        {
            var circle = new EllipseGeometry(new Point(0, 0), ProgressRadius, ProgressRadius);
            circle.Freeze();
            return circle;
        }
        var start = -Math.PI / 2;
        var end = start + FullTurn * clamped;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(ProgressRadius, start), isFilled: false, isClosed: false);
            context.ArcTo(Polar(ProgressRadius, end), new Size(ProgressRadius, ProgressRadius), 0, clamped > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
