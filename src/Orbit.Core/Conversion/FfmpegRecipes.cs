namespace Orbit.Core.Conversion;

public static class FfmpegRecipes
{
    private const string EvenSize = "scale=trunc(iw/2)*2:trunc(ih/2)*2";
    private const string GifFilter =
        "fps=15,scale='min(720,iw)':-1:flags=lanczos,split[frames][copy];[copy]palettegen=stats_mode=diff[palette];[frames][palette]paletteuse=dither=sierra2_4a";

    private static readonly string[] AudioOnlyTargets = ["mp3", "wav", "flac", "ogg", "m4a", "opus", "aac", "wma"];

    public static bool IsAudioOnly(string target)
    {
        return AudioOnlyTargets.Contains(target);
    }

    public static IReadOnlyList<string> StreamCopy(string target)
    {
        var arguments = new List<string> { "-map", "0:v?", "-map", "0:a?", "-c", "copy" };
        if (target is "mp4" or "mov" or "m4v")
        {
            arguments.AddRange(["-movflags", "+faststart"]);
        }
        return arguments;
    }

    public static IReadOnlyList<string> Transcode(string target)
    {
        if (IsAudioOnly(target))
        {
            return ["-map", "0:a:0", "-vn", "-map_metadata", "0", .. AudioCodec(target)];
        }
        return target switch
        {
            "mp4" or "m4v" or "mov" => [.. MainVideoStreams, .. H264, "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
            "mkv" => [.. MainVideoStreams, .. H264, "-c:a", "aac", "-b:a", "192k"],
            "avi" => [.. MainVideoStreams, .. H264, "-c:a", "libmp3lame", "-b:a", "192k"],
            "webm" => [.. MainVideoStreams, .. Vp9, "-c:a", "libopus", "-b:a", "128k"],
            "gif" => ["-map", "0:v:0", "-an", "-vf", GifFilter, "-loop", "0"],
            _ => throw new NotSupportedException($"Не знаю, как сделать {target} через FFmpeg"),
        };
    }

    private static readonly string[] MainVideoStreams = ["-map", "0:v:0?", "-map", "0:a?"];

    private static readonly string[] H264 =
        ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p", "-vf", EvenSize];

    private static readonly string[] Vp9 =
        ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p", "-vf", EvenSize];

    private static string[] AudioCodec(string target)
    {
        return target switch
        {
            "mp3" => ["-c:a", "libmp3lame", "-b:a", "320k"],
            "wav" => ["-c:a", "pcm_s16le"],
            "flac" => ["-c:a", "flac"],
            "ogg" => ["-c:a", "libvorbis", "-q:a", "6"],
            "m4a" => ["-c:a", "aac", "-b:a", "256k"],
            "aac" => ["-c:a", "aac", "-b:a", "256k"],
            "opus" => ["-c:a", "libopus", "-b:a", "160k"],
            "wma" => ["-c:a", "wmav2", "-b:a", "192k"],
            _ => throw new NotSupportedException($"Не знаю звуковой формат {target}"),
        };
    }
}
