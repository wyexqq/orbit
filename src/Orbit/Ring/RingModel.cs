using System.Windows;
using Orbit.Core.Formats;
using Orbit.Interop;

namespace Orbit.Ring;

internal enum RingOpener
{
    Hold,
    Shake,
    Hotkey,
}

internal enum HitKind
{
    Center,
    Main,
    Extra,
}

internal readonly record struct RingHit(HitKind Kind, int Index, double Distance);

internal sealed class RingModel
{
    public const string More = "more";
    private const string NeutralAccent = "#2F6FE0";

    public RingModel(IReadOnlyList<string> files, RingOpener opener, bool buttonHeld, NativePoint openPoint)
    {
        Files = files;
        Opener = opener;
        ButtonHeld = buttonHeld;
        WasSticky = !buttonHeld;
        OpenPoint = openPoint;
        var targets = FormatCatalog.TargetsFor(files);
        var hasMore = targets.Count > RingGeometry.MaxWithoutMore;
        Main = hasMore ? [.. targets.Take(RingGeometry.MainSlots), More] : targets;
        Extras = hasMore ? targets.Skip(RingGeometry.MainSlots).ToList() : [];
        MainLayout = RingGeometry.LayoutMain(Main.Count);
        ExtraLayout = hasMore ? RingGeometry.LayoutExtras(MainLayout[MoreIndex], Extras.Count) : [];
        var category = files.Count > 0 ? FormatCatalog.CategoryOf(FormatCatalog.ExtensionOf(files[0])) : null;
        Accent = category?.Accent ?? NeutralAccent;
        OnAccent = category?.OnAccent ?? "#FFFFFF";
    }

    public IReadOnlyList<string> Files { get; }

    public RingOpener Opener { get; }

    public IReadOnlyList<string> Main { get; }

    public IReadOnlyList<string> Extras { get; }

    public IReadOnlyList<SectorArc> MainLayout { get; }

    public IReadOnlyList<SectorArc> ExtraLayout { get; }

    public string Accent { get; }

    public string OnAccent { get; }

    public NativePoint OpenPoint { get; }

    public NativePoint Center { get; set; }

    public double Scale { get; set; } = 1;

    public bool ButtonHeld { get; set; }

    public bool WasSticky { get; set; }

    public bool ExtrasOpen { get; set; }

    public bool DigitsOnExtras { get; set; }

    public double TravelSinceOpen { get; set; }

    public RingHit? Hovered { get; set; }

    public int MoreIndex => IndexOfMore();

    public bool HasTargets => Main.Count > 0;

    public bool HoversMore => Hovered is { Kind: HitKind.Main } hovered && Main[hovered.Index] == More;

    public Vector OffsetOf(NativePoint point)
    {
        return new Vector((point.X - Center.X) / Scale, (point.Y - Center.Y) / Scale);
    }

    public double DistanceFromOpen(NativePoint point)
    {
        var dx = (point.X - OpenPoint.X) / Scale;
        var dy = (point.Y - OpenPoint.Y) / Scale;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public RingHit? HitAt(NativePoint point)
    {
        var offset = OffsetOf(point);
        var distance = offset.Length;
        var angle = Math.Atan2(offset.Y, offset.X);
        if (distance < RingGeometry.InnerRadius - 2)
        {
            return new RingHit(HitKind.Center, -1, distance);
        }
        if (ExtrasOpen && distance > RingGeometry.OuterRadius + 2)
        {
            return ExtrasHitAt(angle, distance);
        }
        if (distance > RingGeometry.OuterRadius + RingGeometry.Slack || MainLayout.Count == 0)
        {
            return null;
        }
        return new RingHit(HitKind.Main, RingGeometry.SectorIndexAt(angle, MainLayout), distance);
    }

    public string? TargetOf(RingHit? hit)
    {
        return hit switch
        {
            { Kind: HitKind.Main, Index: >= 0 } main when main.Index < Main.Count => Main[main.Index],
            { Kind: HitKind.Extra, Index: >= 0 } extra when extra.Index < Extras.Count => Extras[extra.Index],
            _ => null,
        };
    }

    public RingExtents CurrentExtents()
    {
        return RingGeometry.Extents(ExtrasOpen ? ExtraLayout : []);
    }

    private RingHit? ExtrasHitAt(double angle, double distance)
    {
        var extraIndex = RingGeometry.SectorIndexAt(angle, ExtraLayout);
        if (extraIndex == -1 || distance > RingGeometry.ExtraOuterRadius + RingGeometry.Slack)
        {
            return null;
        }
        if (distance < RingGeometry.ExtraInnerRadius - 2)
        {
            return new RingHit(HitKind.Main, MoreIndex, distance);
        }
        return new RingHit(HitKind.Extra, extraIndex, distance);
    }

    private int IndexOfMore()
    {
        for (var index = 0; index < Main.Count; index++)
        {
            if (Main[index] == More)
            {
                return index;
            }
        }
        return -1;
    }
}
