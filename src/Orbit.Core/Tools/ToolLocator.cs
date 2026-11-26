namespace Orbit.Core.Tools;

public static class ToolLocator
{
    private static readonly Lazy<string?> FfmpegPath = new(() => Find("ffmpeg.exe"));
    private static readonly Lazy<string?> FfprobePath = new(() => Find("ffprobe.exe"));

    public static string? Ffmpeg => FfmpegPath.Value;

    public static string? Ffprobe => FfprobePath.Value;

    private static string? Find(string executable)
    {
        return CandidateFolders()
            .Select(folder => Path.Combine(folder, executable))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> CandidateFolders()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return folder.Trim('"');
        }
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localData, "Microsoft", "WinGet", "Links");
        foreach (var folder in WingetPackageBinFolders(localData))
        {
            yield return folder;
        }
    }

    private static IEnumerable<string> WingetPackageBinFolders(string localData)
    {
        var packages = Path.Combine(localData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages))
        {
            return [];
        }
        try
        {
            return Directory
                .EnumerateDirectories(packages, "*FFmpeg*")
                .SelectMany(package => Directory.EnumerateDirectories(package, "bin", SearchOption.AllDirectories))
                .ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
