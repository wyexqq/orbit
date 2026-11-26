using ImageMagick;
using Orbit.Core.Formats;

namespace Orbit.Core.Conversion;

public sealed class ImageConverter : IFileConverter
{
    private const uint IconMaxSide = 256;
    private const string IconSizes = "256,128,64,48,32,16";
    private static readonly string[] TargetsWithoutTransparency = ["jpg", "jpeg", "bmp"];

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() => Convert(work), cancellation);
    }

    public static void ConvertEncodedImage(byte[] encodedImage, string target, string outputPath)
    {
        using var image = new MagickImage(encodedImage);
        WriteImage(image, target, outputPath);
    }

    private static void Convert(ConversionWork work)
    {
        var sourceExtension = FormatCatalog.ExtensionOf(work.SourcePath);
        var isAnimationToWebp = sourceExtension == "gif" && work.TargetExtension == "webp";
        if (isAnimationToWebp)
        {
            ConvertAnimation(work.SourcePath, work.OutputPath);
            return;
        }
        using var image = ReadFirstFrame(work.SourcePath);
        WriteImage(image, work.TargetExtension, work.OutputPath);
        work.Progress.Report(1);
    }

    private static MagickImage ReadFirstFrame(string path)
    {
        var settings = new MagickReadSettings
        {
            FrameIndex = 0,
            FrameCount = 1,
        };
        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }

    private static void WriteImage(MagickImage image, string target, string outputPath)
    {
        if (target == "ico")
        {
            WriteIcon(image, outputPath);
            return;
        }
        if (TargetsWithoutTransparency.Contains(target) && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
        if (target is "jpg" or "jpeg")
        {
            image.Quality = 92;
        }
        if (target == "webp")
        {
            image.Quality = 90;
        }
        image.Write(outputPath, FormatFor(target));
    }

    private static void WriteIcon(MagickImage image, string outputPath)
    {
        var side = Math.Max(image.Width, image.Height);
        image.BackgroundColor = MagickColors.Transparent;
        image.Extent(side, side, Gravity.Center);
        if (side > IconMaxSide)
        {
            image.Resize(IconMaxSide, IconMaxSide);
        }
        image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", IconSizes);
        image.Write(outputPath, MagickFormat.Icon);
    }

    private static void ConvertAnimation(string sourcePath, string outputPath)
    {
        using var frames = new MagickImageCollection(sourcePath);
        frames.Coalesce();
        foreach (var frame in frames)
        {
            frame.Quality = 80;
        }
        frames.Write(outputPath, MagickFormat.WebP);
    }

    private static MagickFormat FormatFor(string target)
    {
        return target switch
        {
            "png" => MagickFormat.Png,
            "jpg" or "jpeg" => MagickFormat.Jpeg,
            "webp" => MagickFormat.WebP,
            "bmp" => MagickFormat.Bmp,
            "gif" => MagickFormat.Gif,
            "tiff" => MagickFormat.Tiff,
            "avif" => MagickFormat.Avif,
            "pdf" => MagickFormat.Pdf,
            _ => throw new NotSupportedException($"Не умею сохранять картинку в {target}"),
        };
    }
}
