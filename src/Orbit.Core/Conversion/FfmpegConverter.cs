using System.Globalization;
using Orbit.Core.Files;
using Orbit.Core.Formats;
using Orbit.Core.Tools;

namespace Orbit.Core.Conversion;

public sealed class FfmpegConverter : IFileConverter
{
    private const string MissingFfmpegMessage = "Не нашёл FFmpeg. Поставьте его: winget install Gyan.FFmpeg";

    public async Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        var ffmpeg = ToolLocator.Ffmpeg ?? throw new InvalidOperationException(MissingFfmpegMessage);
        var duration = await ProbeDurationAsync(work.SourcePath, cancellation);
        var sourceExtension = FormatCatalog.ExtensionOf(work.SourcePath);
        if (FormatCatalog.CanStreamCopy(sourceExtension, work.TargetExtension))
        {
            var copied = await TryRunAsync(ffmpeg, work, FfmpegRecipes.StreamCopy(work.TargetExtension), duration, cancellation);
            if (copied.ExitCode == 0 && HasContent(work.OutputPath))
            {
                return;
            }
            Log.Info($"Без перекодирования не вышло, перекодирую: {copied.ErrorTail}");
            TemporaryFiles.Delete(work.OutputPath);
        }
        var transcoded = await TryRunAsync(ffmpeg, work, FfmpegRecipes.Transcode(work.TargetExtension), duration, cancellation);
        if (transcoded.ExitCode != 0 || !HasContent(work.OutputPath))
        {
            throw new InvalidOperationException(DescribeFailure(transcoded.ErrorTail));
        }
    }

    private static async Task<ProcessOutcome> TryRunAsync(
        string ffmpeg,
        ConversionWork work,
        IReadOnlyList<string> recipe,
        TimeSpan? duration,
        CancellationToken cancellation)
    {
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", work.SourcePath };
        arguments.AddRange(recipe);
        arguments.AddRange(["-progress", "pipe:1", "-nostats", work.OutputPath]);
        return await ExternalProcess.RunAsync(
            ffmpeg,
            arguments,
            line => ReportProgress(line, duration, work.Progress),
            cancellation);
    }

    private static void ReportProgress(string line, TimeSpan? duration, IProgress<double> progress)
    {
        if (duration is null || duration.Value.TotalMicroseconds <= 0)
        {
            return;
        }
        var separator = line.IndexOf('=');
        if (separator < 0)
        {
            return;
        }
        var key = line[..separator];
        var value = line[(separator + 1)..];
        if (key is not ("out_time_us" or "out_time_ms"))
        {
            return;
        }
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds) || microseconds < 0)
        {
            return;
        }
        progress.Report(Math.Clamp(microseconds / duration.Value.TotalMicroseconds, 0, 0.99));
    }

    private static async Task<TimeSpan?> ProbeDurationAsync(string path, CancellationToken cancellation)
    {
        var ffprobe = ToolLocator.Ffprobe;
        if (ffprobe is null)
        {
            return null;
        }
        string[] arguments = ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path];
        var outcome = await ExternalProcess.RunAsync(ffprobe, arguments, null, cancellation);
        var text = outcome.StandardOutput.Trim();
        if (outcome.ExitCode == 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }
        return null;
    }

    private static bool HasContent(string path)
    {
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static string DescribeFailure(string errorTail)
    {
        if (errorTail.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
            || errorTail.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            return "В файле нет нужной дорожки (например, звука)";
        }
        var lastLine = errorTail.Split(Environment.NewLine).LastOrDefault(line => line.Length > 0);
        return lastLine is null ? "FFmpeg не смог перекодировать файл" : $"FFmpeg: {lastLine}";
    }
}
