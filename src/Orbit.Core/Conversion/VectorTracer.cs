using System.Globalization;
using System.Text;
using ImageMagick;

namespace Orbit.Core.Conversion;

public sealed class VectorTracer : IFileConverter
{
    private const uint MaxTraceSide = 1024;
    private const uint PaletteSize = 16;
    private const byte OpaqueAlphaThreshold = 128;
    private const double MinimumLoopArea = 6;
    private const double SimplifyTolerance = 0.8;
    private const double SharpTurnDegrees = 55;

    private const byte Right = 1;
    private const byte Down = 2;
    private const byte Left = 4;
    private const byte Up = 8;

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() => Trace(work, cancellation), cancellation);
    }

    private static void Trace(ConversionWork work, CancellationToken cancellation)
    {
        using var image = new MagickImage(work.SourcePath, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
        image.AutoOrient();
        var originalWidth = image.Width;
        var originalHeight = image.Height;
        if (Math.Max(image.Width, image.Height) > MaxTraceSide)
        {
            image.Resize(new MagickGeometry(MaxTraceSide, MaxTraceSide));
        }
        image.Quantize(new QuantizeSettings { Colors = PaletteSize, DitherMethod = DitherMethod.No });
        var width = (int)image.Width;
        var height = (int)image.Height;
        var (labels, palette) = LabelPixels(image, width, height);
        var layerOrder = OrderLayersByArea(labels, palette.Count);
        var pathDataByLayer = TraceStackedLayers(labels, width, height, layerOrder, work.Progress, cancellation);
        var svg = BuildSvg(layerOrder, pathDataByLayer, palette, originalWidth, originalHeight, width, height);
        File.WriteAllText(work.OutputPath, svg, new UTF8Encoding(false));
        work.Progress.Report(1);
    }

    private static (int[] Labels, List<uint> Palette) LabelPixels(MagickImage image, int width, int height)
    {
        using var pixels = image.GetPixelsUnsafe();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidDataException("Не смог прочитать пиксели");
        var labels = new int[width * height];
        var palette = new List<uint>();
        var indexByColor = new Dictionary<uint, int>();
        for (var pixel = 0; pixel < labels.Length; pixel++)
        {
            var offset = pixel * 4;
            if (rgba[offset + 3] < OpaqueAlphaThreshold)
            {
                labels[pixel] = -1;
                continue;
            }
            var color = (uint)((rgba[offset] << 16) | (rgba[offset + 1] << 8) | rgba[offset + 2]);
            if (!indexByColor.TryGetValue(color, out var index))
            {
                index = palette.Count;
                indexByColor[color] = index;
                palette.Add(color);
            }
            labels[pixel] = index;
        }
        return (labels, palette);
    }

    private static List<int> OrderLayersByArea(int[] labels, int layerCount)
    {
        var areas = new int[layerCount];
        foreach (var label in labels)
        {
            if (label >= 0)
            {
                areas[label] += 1;
            }
        }
        return Enumerable.Range(0, layerCount).OrderByDescending(layer => areas[layer]).ToList();
    }

    private static Dictionary<int, string> TraceStackedLayers(
        int[] labels,
        int width,
        int height,
        List<int> layerOrder,
        IProgress<double> progress,
        CancellationToken cancellation)
    {
        var stackedMask = new bool[labels.Length];
        var pathDataByLayer = new Dictionary<int, string>();
        for (var rank = layerOrder.Count - 1; rank >= 0; rank--)
        {
            cancellation.ThrowIfCancellationRequested();
            var layer = layerOrder[rank];
            for (var pixel = 0; pixel < labels.Length; pixel++)
            {
                if (labels[pixel] == layer)
                {
                    stackedMask[pixel] = true;
                }
            }
            var loops = TraceLoops(stackedMask, width, height);
            pathDataByLayer[layer] = PathDataFor(loops);
            var tracedLayers = layerOrder.Count - rank;
            progress.Report(0.95 * tracedLayers / layerOrder.Count);
        }
        return pathDataByLayer;
    }

    private static List<List<GridPoint>> TraceLoops(bool[] inside, int width, int height)
    {
        var stride = width + 1;
        var outgoing = new byte[stride * (height + 1)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!inside[y * width + x])
                {
                    continue;
                }
                if (y == 0 || !inside[(y - 1) * width + x])
                {
                    outgoing[y * stride + x] |= Right;
                }
                if (x == width - 1 || !inside[y * width + x + 1])
                {
                    outgoing[y * stride + x + 1] |= Down;
                }
                if (y == height - 1 || !inside[(y + 1) * width + x])
                {
                    outgoing[(y + 1) * stride + x + 1] |= Left;
                }
                if (x == 0 || !inside[y * width + x - 1])
                {
                    outgoing[(y + 1) * stride + x] |= Up;
                }
            }
        }
        var loops = new List<List<GridPoint>>();
        for (var vertex = 0; vertex < outgoing.Length; vertex++)
        {
            while (outgoing[vertex] != 0)
            {
                var loop = FollowLoop(outgoing, stride, vertex);
                if (Math.Abs(SignedArea(loop)) >= MinimumLoopArea)
                {
                    loops.Add(loop);
                }
            }
        }
        return loops;
    }

    private static List<GridPoint> FollowLoop(byte[] outgoing, int stride, int startVertex)
    {
        var corners = new List<GridPoint>();
        var vertex = startVertex;
        var direction = LowestDirection(outgoing[vertex]);
        byte previousDirection = 0;
        while (direction != 0)
        {
            outgoing[vertex] &= (byte)~direction;
            if (direction != previousDirection)
            {
                corners.Add(new GridPoint(vertex % stride, vertex / stride));
            }
            previousDirection = direction;
            vertex = Step(vertex, direction, stride);
            if (vertex == startVertex)
            {
                break;
            }
            direction = ChooseNextDirection(outgoing[vertex], direction);
        }
        return corners;
    }

    private static byte LowestDirection(byte directions)
    {
        return (byte)(directions & -directions);
    }

    private static byte ChooseNextDirection(byte available, byte current)
    {
        byte[] preferences = [TurnRight(current), current, TurnLeft(current)];
        foreach (var candidate in preferences)
        {
            if ((available & candidate) != 0)
            {
                return candidate;
            }
        }
        return 0;
    }

    private static byte TurnRight(byte direction)
    {
        return direction == Up ? Right : (byte)(direction << 1);
    }

    private static byte TurnLeft(byte direction)
    {
        return direction == Right ? Up : (byte)(direction >> 1);
    }

    private static int Step(int vertex, byte direction, int stride)
    {
        return direction switch
        {
            Right => vertex + 1,
            Down => vertex + stride,
            Left => vertex - 1,
            _ => vertex - stride,
        };
    }

    private static double SignedArea(List<GridPoint> loop)
    {
        double doubledArea = 0;
        for (var index = 0; index < loop.Count; index++)
        {
            var current = loop[index];
            var next = loop[(index + 1) % loop.Count];
            doubledArea += (double)current.X * next.Y - (double)next.X * current.Y;
        }
        return doubledArea / 2;
    }

    private static string PathDataFor(List<List<GridPoint>> loops)
    {
        var path = new StringBuilder();
        foreach (var loop in loops)
        {
            AppendSmoothLoop(path, Simplify(loop));
        }
        return path.ToString();
    }

    private static List<PointD> Simplify(List<GridPoint> corners)
    {
        var points = corners.Select(corner => new PointD(corner.X, corner.Y)).ToList();
        if (points.Count < 4)
        {
            return points;
        }
        var farthestIndex = IndexOfFarthest(points, points[0]);
        var firstHalf = points.GetRange(0, farthestIndex + 1);
        var secondHalf = points.GetRange(farthestIndex, points.Count - farthestIndex);
        secondHalf.Add(points[0]);
        var simplified = DouglasPeucker(firstHalf);
        var simplifiedSecond = DouglasPeucker(secondHalf);
        simplified.RemoveAt(simplified.Count - 1);
        simplifiedSecond.RemoveAt(simplifiedSecond.Count - 1);
        simplified.AddRange(simplifiedSecond);
        return simplified;
    }

    private static int IndexOfFarthest(List<PointD> points, PointD from)
    {
        var farthestIndex = 0;
        var farthestDistance = 0.0;
        for (var index = 1; index < points.Count; index++)
        {
            var distance = Distance(points[index], from);
            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthestIndex = index;
            }
        }
        return farthestIndex;
    }

    private static List<PointD> DouglasPeucker(List<PointD> points)
    {
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            var (start, end) = ranges.Pop();
            var farthestIndex = -1;
            var farthestDistance = 0.0;
            for (var index = start + 1; index < end; index++)
            {
                var distance = DistanceToSegment(points[index], points[start], points[end]);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = index;
                }
            }
            if (farthestIndex != -1 && farthestDistance > SimplifyTolerance)
            {
                keep[farthestIndex] = true;
                ranges.Push((start, farthestIndex));
                ranges.Push((farthestIndex, end));
            }
        }
        return points.Where((_, index) => keep[index]).ToList();
    }

    private static void AppendSmoothLoop(StringBuilder path, List<PointD> points)
    {
        var count = points.Count;
        if (count < 3)
        {
            return;
        }
        path.Append('M').Append(Format(Midpoint(points[count - 1], points[0])));
        for (var index = 0; index < count; index++)
        {
            var previous = points[(index - 1 + count) % count];
            var corner = points[index];
            var next = points[(index + 1) % count];
            var segmentEnd = Midpoint(corner, next);
            if (IsSharpTurn(previous, corner, next))
            {
                path.Append('L').Append(Format(corner)).Append('L').Append(Format(segmentEnd));
            }
            else
            {
                path.Append('Q').Append(Format(corner)).Append(' ').Append(Format(segmentEnd));
            }
        }
        path.Append('Z');
    }

    private static bool IsSharpTurn(PointD previous, PointD corner, PointD next)
    {
        var incomingX = corner.X - previous.X;
        var incomingY = corner.Y - previous.Y;
        var outgoingX = next.X - corner.X;
        var outgoingY = next.Y - corner.Y;
        var lengths = Math.Sqrt(incomingX * incomingX + incomingY * incomingY) * Math.Sqrt(outgoingX * outgoingX + outgoingY * outgoingY);
        if (lengths == 0)
        {
            return true;
        }
        var cosine = Math.Clamp((incomingX * outgoingX + incomingY * outgoingY) / lengths, -1, 1);
        var turnDegrees = Math.Acos(cosine) * 180 / Math.PI;
        return turnDegrees > SharpTurnDegrees;
    }

    private static string BuildSvg(
        List<int> layerOrder,
        Dictionary<int, string> pathDataByLayer,
        List<uint> palette,
        uint originalWidth,
        uint originalHeight,
        int width,
        int height)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{originalWidth}\" height=\"{originalHeight}\" viewBox=\"0 0 {width} {height}\">\n");
        foreach (var layer in layerOrder)
        {
            var pathData = pathDataByLayer[layer];
            if (pathData.Length == 0)
            {
                continue;
            }
            svg.Append(CultureInfo.InvariantCulture,
                $"<path fill=\"#{palette[layer]:X6}\" fill-rule=\"evenodd\" d=\"{pathData}\"/>\n");
        }
        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static PointD Midpoint(PointD first, PointD second)
    {
        return new PointD((first.X + second.X) / 2, (first.Y + second.Y) / 2);
    }

    private static double Distance(PointD first, PointD second)
    {
        return Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));
    }

    private static double DistanceToSegment(PointD point, PointD start, PointD end)
    {
        var segmentX = end.X - start.X;
        var segmentY = end.Y - start.Y;
        var lengthSquared = segmentX * segmentX + segmentY * segmentY;
        if (lengthSquared == 0)
        {
            return Distance(point, start);
        }
        var along = Math.Clamp(((point.X - start.X) * segmentX + (point.Y - start.Y) * segmentY) / lengthSquared, 0, 1);
        var projection = new PointD(start.X + along * segmentX, start.Y + along * segmentY);
        return Distance(point, projection);
    }

    private static string Format(PointD point)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{point.X:0.##} {point.Y:0.##}");
    }

    private readonly record struct GridPoint(int X, int Y);

    private readonly record struct PointD(double X, double Y);
}
