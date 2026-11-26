using System.Runtime.InteropServices;

namespace Orbit.Core.Files;

public static class ShellNotify
{
    private const int ItemRenamed = 0x00000001;
    private const int ItemCreated = 0x00000002;
    private const int ItemDeleted = 0x00000004;
    private const int FolderCreated = 0x00000008;
    private const int FolderDeleted = 0x00000010;
    private const int FolderRenamed = 0x00020000;
    private const uint ArgumentsArePaths = 0x0005;
    private const uint DeliverWithoutWaiting = 0x3000;

    public static void Added(string path)
    {
        var change = Directory.Exists(path) ? FolderCreated : ItemCreated;
        Send(change, path, null);
    }

    public static void RemovedFile(string path)
    {
        Send(ItemDeleted, path, null);
    }

    public static void RemovedFolder(string path)
    {
        Send(FolderDeleted, path, null);
    }

    public static void Renamed(string oldPath, string newPath)
    {
        var change = Directory.Exists(newPath) ? FolderRenamed : ItemRenamed;
        Send(change, oldPath, newPath);
    }

    private static void Send(int change, string path, string? secondPath)
    {
        SHChangeNotify(change, ArgumentsArePaths | DeliverWithoutWaiting, path, secondPath);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string path, string? secondPath);
}
