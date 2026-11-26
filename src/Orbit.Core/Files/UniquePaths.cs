namespace Orbit.Core.Files;

public static class UniquePaths
{
    public static string ForFile(string directory, string baseName, string extension)
    {
        return FirstFree(suffix => Path.Combine(directory, $"{baseName}{suffix}.{extension}"));
    }

    public static string ForFolder(string directory, string name)
    {
        return FirstFree(suffix => Path.Combine(directory, $"{name}{suffix}"));
    }

    private static string FirstFree(Func<string, string> compose)
    {
        var candidate = compose(string.Empty);
        var counter = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = compose($" ({counter})");
            counter += 1;
        }
        return candidate;
    }
}
