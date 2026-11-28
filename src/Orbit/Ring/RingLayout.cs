using Orbit.Interop;

namespace Orbit.Ring;

internal readonly record struct RingPlace(NativePoint Center, double Scale, NativeRect WorkArea, RingSize Size);

internal sealed record RingLayout(
    IReadOnlyList<string> Main,
    IReadOnlyList<string> Extras,
    IReadOnlyList<SectorArc> MainArcs,
    IReadOnlyList<SectorArc> ExtraArcs)
{
    private const double Degree = Math.PI / 180;
    private const double NarrowestMain = 30 * Degree;
    private const double WidestMain = 60 * Degree;
    private const double WidestExtra = 40 * Degree;
    private const double ExtrasLimit = 300 * Degree;
    private const double ReachMargin = 4;
    private const int FullRingSlots = 6;
    private const int FewestSlots = 2;

    public static RingLayout Plan(IReadOnlyList<string> targets, RingPlace place)
    {
        var mainRoom = ScreenRoom.FreeArc(place, place.Size.Outer + ReachMargin);
        var slots = mainRoom is SectorArc room ? SlotsIn(room) : FullRingSlots;
        var (main, extras) = Split(targets, slots);
        var mainArcs = mainRoom is SectorArc clipped ? MainInRoom(main.Count, clipped) : MainAround(main.Count);
        if (extras.Count == 0)
        {
            return new RingLayout(main, extras, mainArcs, []);
        }
        var moreSector = mainArcs[^1];
        var extraRoom = ScreenRoom.FreeArc(place, place.Size.ExtraOuter + ReachMargin);
        var extraArcs = extraRoom is SectorArc extraClipped
            ? ExtrasInRoom(moreSector, extras.Count, extraClipped)
            : ExtrasAround(moreSector, extras.Count);
        return new RingLayout(main, extras, mainArcs, extraArcs);
    }

    private static int SlotsIn(SectorArc room)
    {
        var fitting = (int)Math.Floor(room.Span / NarrowestMain);
        return Math.Clamp(fitting, FewestSlots, FullRingSlots);
    }

    private static (IReadOnlyList<string> Main, IReadOnlyList<string> Extras) Split(IReadOnlyList<string> targets, int slots)
    {
        if (targets.Count <= slots)
        {
            return (targets, []);
        }
        var shownBeforeMore = slots - 1;
        IReadOnlyList<string> main = [.. targets.Take(shownBeforeMore), RingModel.More];
        IReadOnlyList<string> extras = targets.Skip(shownBeforeMore).ToList();
        return (main, extras);
    }

    private static IReadOnlyList<SectorArc> MainAround(int count)
    {
        if (count == 0)
        {
            return [];
        }
        var span = RingGeometry.FullTurn / count;
        return RingGeometry.SectorsFrom(-Math.PI / 2 - span / 2, span, count);
    }

    private static IReadOnlyList<SectorArc> MainInRoom(int count, SectorArc room)
    {
        if (count == 0)
        {
            return [];
        }
        var span = Math.Min(WidestMain, room.Span / count);
        var used = span * count;
        var start = room.Start + (room.Span - used) / 2;
        return RingGeometry.SectorsFrom(start, span, count);
    }

    private static IReadOnlyList<SectorArc> ExtrasAround(SectorArc moreSector, int count)
    {
        var preferred = Math.Min(WidestExtra, ExtrasLimit / count);
        var span = Math.Max(preferred, moreSector.Span / count);
        return RingGeometry.SectorsFrom(moreSector.Middle - span * count / 2, span, count);
    }

    private static IReadOnlyList<SectorArc> ExtrasInRoom(SectorArc moreSector, int count, SectorArc room)
    {
        var span = Math.Min(WidestExtra, room.Span / count);
        var used = span * count;
        var moreMiddle = RingGeometry.AngleNear(moreSector.Middle, room.Middle);
        var start = RingGeometry.ClampBetween(moreMiddle - used / 2, room.Start, room.End - used);
        return RingGeometry.SectorsFrom(start, span, count);
    }
}

internal static class ScreenRoom
{
    private const double EdgeMargin = 6;
    private const int Steps = 720;

    public static SectorArc? FreeArc(RingPlace place, double reach)
    {
        var blocked = BlockedByEdges(place, reach);
        if (blocked.Count == 0)
        {
            return null;
        }
        var step = RingGeometry.FullTurn / Steps;
        var free = new bool[Steps];
        for (var index = 0; index < Steps; index++)
        {
            var angle = index * step;
            free[index] = !blocked.Any(arc => Covers(arc, angle));
        }
        var (start, length) = LongestRun(free);
        if (length == 0 || length == Steps)
        {
            return null;
        }
        return new SectorArc(start * step, (start + length) * step);
    }

    private static List<SectorArc> BlockedByEdges(RingPlace place, double reach)
    {
        var area = place.WorkArea;
        var center = place.Center;
        var scale = place.Scale;
        var edges = new (double Room, double Facing)[]
        {
            ((center.X - area.Left) / scale - EdgeMargin, Math.PI),
            ((area.Right - center.X) / scale - EdgeMargin, 0),
            ((center.Y - area.Top) / scale - EdgeMargin, -Math.PI / 2),
            ((area.Bottom - center.Y) / scale - EdgeMargin, Math.PI / 2),
        };
        var blocked = new List<SectorArc>();
        foreach (var (room, facing) in edges)
        {
            if (room >= reach)
            {
                continue;
            }
            var halfWidth = Math.Acos(Math.Clamp(room / reach, -1, 1));
            blocked.Add(new SectorArc(facing - halfWidth, facing + halfWidth));
        }
        return blocked;
    }

    private static bool Covers(SectorArc arc, double angle)
    {
        return RingGeometry.NormalizeAngle(angle - arc.Start) <= arc.Span;
    }

    private static (int Start, int Length) LongestRun(bool[] free)
    {
        var bestStart = 0;
        var bestLength = 0;
        for (var start = 0; start < free.Length; start++)
        {
            var previousIsFree = free[(start - 1 + free.Length) % free.Length];
            if (!free[start] || previousIsFree)
            {
                continue;
            }
            var length = 0;
            while (length < free.Length && free[(start + length) % free.Length])
            {
                length++;
            }
            if (length > bestLength)
            {
                bestStart = start;
                bestLength = length;
            }
        }
        return (bestStart, bestLength);
    }
}
