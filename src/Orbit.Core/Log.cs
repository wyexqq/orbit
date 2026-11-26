namespace Orbit.Core;

public static class Log
{
    private static readonly object Gate = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Orbit",
        "orbit.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception error)
    {
        Write("ERROR", $"{message}: {error}");
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, line);
            }
        }
        catch (IOException)
        {
            Console.Error.Write(line);
        }
    }
}
