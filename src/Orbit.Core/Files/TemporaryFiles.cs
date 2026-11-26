namespace Orbit.Core.Files;

public static class TemporaryFiles
{
    private const string WorkFolderPrefix = ".orbit-";
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Orbit");

    public static string NewPath(string extension)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, $"{Guid.NewGuid():N}.{extension}");
    }

    public static string NewSiblingPath(string nearPath, string extension)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var directory = Path.GetDirectoryName(nearPath)!;
        var workFolder = Directory.CreateDirectory(Path.Combine(directory, WorkFolderPrefix + id));
        workFolder.Attributes |= FileAttributes.Hidden;
        var name = extension.Length == 0 ? $"orbit-{id}" : $"orbit-{id}.{extension}";
        return Path.Combine(workFolder.FullName, name);
    }

    public static void DeleteWorkFolder(string siblingPath)
    {
        var workFolder = Path.GetDirectoryName(siblingPath)!;
        if (!Path.GetFileName(workFolder).StartsWith(WorkFolderPrefix, StringComparison.Ordinal))
        {
            Log.Info($"Не трогаю {workFolder}: это не временная папка Orbit");
            return;
        }
        try
        {
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, recursive: true);
                ShellNotify.RemovedFolder(workFolder);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Не удалил временную папку {workFolder}", error);
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            var wordImagesFolder = Path.Combine(
                Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "_files");
            if (Directory.Exists(wordImagesFolder))
            {
                Directory.Delete(wordImagesFolder, recursive: true);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Не удалил временный файл {path}", error);
        }
    }
}
