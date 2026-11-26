using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Orbit.Core.Files;

public static class RecycleBin
{
    private const uint DeleteOperation = 3;
    private const ushort SilentFlag = 0x0004;
    private const ushort NoConfirmationFlag = 0x0010;
    private const ushort AllowUndoFlag = 0x0040;
    private const ushort NoErrorUiFlag = 0x0400;
    private const int PathFieldOffset = 24;
    private const int LegacyPathLength = 260;

    public static bool IsAvailableFor(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }
        return new DriveInfo(root).DriveType == DriveType.Fixed;
    }

    public static void Send(string path)
    {
        var operation = new ShellFileOperation
        {
            Function = DeleteOperation,
            From = path + '\0',
            Flags = SilentFlag | NoConfirmationFlag | AllowUndoFlag | NoErrorUiFlag,
        };
        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.AnyOperationsAborted)
        {
            throw new IOException($"Не получилось убрать в Корзину {Path.GetFileName(path)} (код {result})");
        }
    }

    public static bool Restore(string originalPath)
    {
        var binFolder = BinFolderFor(originalPath);
        if (binFolder is null || !Directory.Exists(binFolder))
        {
            return false;
        }
        if (File.Exists(originalPath) || Directory.Exists(originalPath))
        {
            return false;
        }
        var infoFiles = new DirectoryInfo(binFolder)
            .EnumerateFiles("$I*")
            .OrderByDescending(file => file.LastWriteTimeUtc);
        foreach (var infoFile in infoFiles)
        {
            var deletedFrom = ReadOriginalPath(infoFile.FullName);
            if (!string.Equals(deletedFrom, originalPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var dataPath = Path.Combine(binFolder, "$R" + infoFile.Name[2..]);
            if (Directory.Exists(dataPath))
            {
                Directory.Move(dataPath, originalPath);
            }
            else
            {
                File.Move(dataPath, originalPath);
            }
            infoFile.Delete();
            return true;
        }
        return false;
    }

    private static string? BinFolderFor(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        var userSid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(root) || userSid is null)
        {
            return null;
        }
        return Path.Combine(root, "$Recycle.Bin", userSid);
    }

    private static string? ReadOriginalPath(string infoPath)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(infoPath), Encoding.Unicode);
            var version = reader.ReadInt64();
            reader.BaseStream.Position = PathFieldOffset;
            var length = version >= 2 ? reader.ReadInt32() : LegacyPathLength;
            return new string(reader.ReadChars(length)).TrimEnd('\0');
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShellFileOperation operation);
}
