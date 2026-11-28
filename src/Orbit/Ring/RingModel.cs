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

    public RingModel(IReadOnlyList<string> files, RingOpener opener, bool buttonHeld, NativePoint openPoint, RingPlace place)
    {
        Files = files;
        Opener = opener;
        ButtonHeld = buttonHeld;
        WasSticky = !buttonHeld;
        OpenPoint = openPoint;
        Place = place;
        var layout = RingLayout.Plan(FormatCatalog.TargetsFor(files), place);
        Main = layout.Main;
        Extras = layout.Extras;
        MainLayout = layout.MainArcs;
        ExtraLayout = layout.ExtraArcs;
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

    public RingPlace Place { get; }

    public NativePoint Center => Place.Center;

    public double Scale => Place.Scale;

    public RingSize Size => Place.Size;

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
        if (distance < Size.Inner - 2)
        {
            return new RingHit(HitKind.Center, -1, distance);
        }
        if (ExtrasOpen && distance > Size.Outer + 2)
        {
            return ExtrasHitAt(angle, distance);
        }
        if (distance > Size.Outer + RingGeometry.Slack)
        {
            return null;
        }
        var index = RingGeometry.SectorIndexAt(angle, MainLayout);
        return index == -1 ? null : new RingHit(HitKind.Main, index, distance);
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

    private RingHit? ExtrasHitAt(double angle, double distance)
    {
        var extraIndex = RingGeometry.SectorIndexAt(angle, ExtraLayout);
        if (extraIndex == -1 || distance > Size.ExtraOuter + RingGeometry.Slack)
        {
            return null;
        }
        if (distance < Size.ExtraInner - 2)
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
