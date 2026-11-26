using SkiaSharp;
using Svg.Skia;

namespace Orbit.Core.Conversion;

public sealed class SvgConverter : IFileConverter
{
    private const float PreferredLongestSide = 2048;

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() => Convert(work), cancellation);
    }

    private static void Convert(ConversionWork work)
    {
        using var svg = new SKSvg();
        var picture = svg.Load(work.SourcePath) ?? throw new InvalidDataException("Не смог прочитать SVG");
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidDataException("У SVG нет размеров");
        }
        if (work.TargetExtension == "pdf")
        {
            WritePdf(picture, bounds, work.OutputPath);
        }
        else
        {
            var pngBytes = RenderPng(picture, bounds, work.TargetExtension == "jpg");
            WriteRaster(pngBytes, work.TargetExtension, work.OutputPath);
        }
        work.Progress.Report(1);
    }

    private static void WritePdf(SKPicture picture, SKRect bounds, string outputPath)
    {
        using var stream = File.Create(outputPath);
        using var document = SKDocument.CreatePdf(stream);
        var canvas = document.BeginPage(bounds.Width, bounds.Height);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        document.EndPage();
        document.Close();
    }

    private static byte[] RenderPng(SKPicture picture, SKRect bounds, bool needsWhiteBackground)
    {
        var longestSide = Math.Max(bounds.Width, bounds.Height);
        var scale = longestSide < PreferredLongestSide ? PreferredLongestSide / longestSide : 1f;
        var width = (int)Math.Ceiling(bounds.Width * scale);
        var height = (int)Math.Ceiling(bounds.Height * scale);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(needsWhiteBackground ? SKColors.White : SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void WriteRaster(byte[] pngBytes, string target, string outputPath)
    {
        if (target == "png")
        {
            File.WriteAllBytes(outputPath, pngBytes);
            return;
        }
        ImageConverter.ConvertEncodedImage(pngBytes, target, outputPath);
    }
}
