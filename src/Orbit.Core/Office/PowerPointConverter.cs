using System.Runtime.InteropServices;
using Orbit.Core.Conversion;

namespace Orbit.Core.Office;

public sealed class PowerPointConverter : IFileConverter
{
    private const int MsoTrue = -1;
    private const int MsoFalse = 0;
    private const int SlideImageWidth = 1920;
    private const int VideoHeight = 1080;
    private const int VideoFramesPerSecond = 30;
    private const int VideoQuality = 85;
    private const int SecondsPerSlideWithoutTimings = 3;
    private const int VideoDone = 3;
    private const int VideoFailed = 4;
    private static readonly TimeSpan VideoPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan VideoTimeout = TimeSpan.FromMinutes(30);

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        TimeSpan? timeout = work.TargetExtension == "mp4" ? VideoTimeout : null;
        return OfficeHost.PowerPoint.RunAsync(powerPoint => Convert(powerPoint, work, cancellation), cancellation, timeout);
    }

    private static void Convert(dynamic powerPoint, ConversionWork work, CancellationToken cancellation)
    {
        var presentation = powerPoint.Presentations.Open(work.SourcePath, MsoTrue, MsoFalse, MsoFalse);
        try
        {
            switch (work.TargetExtension)
            {
                case "png":
                case "jpg":
                    ExportSlides(presentation, work, cancellation);
                    break;
                case "mp4":
                    CreateVideo(presentation, work.OutputPath, cancellation);
                    break;
                default:
                    presentation.SaveAs(work.OutputPath, FormatFor(work.TargetExtension));
                    break;
            }
        }
        finally
        {
            presentation.Close();
            object releasedPresentation = presentation;
            Marshal.FinalReleaseComObject(releasedPresentation);
        }
        work.Progress.Report(1);
    }

    private static void ExportSlides(dynamic presentation, ConversionWork work, CancellationToken cancellation)
    {
        Directory.CreateDirectory(work.OutputPath);
        var baseName = Path.GetFileNameWithoutExtension(work.SourcePath);
        var filter = work.TargetExtension == "png" ? "PNG" : "JPG";
        var slideCount = (int)presentation.Slides.Count;
        var slideWidth = (double)presentation.PageSetup.SlideWidth;
        var slideHeight = (double)presentation.PageSetup.SlideHeight;
        var imageHeight = (int)Math.Round(SlideImageWidth * slideHeight / slideWidth);
        var digits = Math.Max(2, slideCount.ToString().Length);
        for (var number = 1; number <= slideCount; number++)
        {
            cancellation.ThrowIfCancellationRequested();
            var slide = presentation.Slides[number];
            var imageName = $"{baseName} {number.ToString().PadLeft(digits, '0')}.{work.TargetExtension}";
            slide.Export(Path.Combine(work.OutputPath, imageName), filter, SlideImageWidth, imageHeight);
            object releasedSlide = slide;
            Marshal.FinalReleaseComObject(releasedSlide);
            work.Progress.Report((double)number / slideCount);
        }
    }

    private static void CreateVideo(dynamic presentation, string outputPath, CancellationToken cancellation)
    {
        presentation.CreateVideo(outputPath, true, SecondsPerSlideWithoutTimings, VideoHeight, VideoFramesPerSecond, VideoQuality);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var status = (int)presentation.CreateVideoStatus;
            if (status == VideoDone)
            {
                return;
            }
            if (status == VideoFailed)
            {
                throw new InvalidOperationException("PowerPoint не смог собрать видео");
            }
            Thread.Sleep(VideoPollInterval);
        }
    }

    private static int FormatFor(string target)
    {
        return target switch
        {
            "pptx" => 24,
            "ppt" => 1,
            "pdf" => 32,
            "odp" => 35,
            _ => throw new NotSupportedException($"PowerPoint не сохраняет в {target}"),
        };
    }
}
