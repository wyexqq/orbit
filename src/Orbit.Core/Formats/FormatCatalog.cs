namespace Orbit.Core.Formats;

public static class FormatCatalog
{
    public static IReadOnlyList<FormatCategory> Categories { get; } = CreateCategories();

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["jpg"] = "jpeg",
        ["jpeg"] = "jpg",
        ["txt"] = "md",
        ["md"] = "txt",
    };

    private static readonly Dictionary<string, FormatCategory> CategoryByExtension = Categories
        .SelectMany(category => category.Sources.Select(extension => (extension, category)))
        .ToDictionary(pair => pair.extension, pair => pair.category);

    public static string ExtensionOf(string path)
    {
        return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    }

    public static FormatCategory? CategoryOf(string extension)
    {
        return CategoryByExtension.GetValueOrDefault(extension);
    }

    public static bool IsConvertibleFile(string path)
    {
        return File.Exists(path) && CategoryOf(ExtensionOf(path)) is not null;
    }

    public static bool IsRename(string sourceExtension, string target)
    {
        return Aliases.TryGetValue(sourceExtension, out var alias) && alias == target;
    }

    public static bool ProducesFolder(string sourceExtension, string target)
    {
        var category = CategoryOf(sourceExtension);
        return category is not null && category.FolderTargets.Contains(target);
    }

    public static bool CanStreamCopy(string sourceExtension, string target)
    {
        var group = CategoryOf(sourceExtension)?.StreamCopyGroup ?? [];
        return group.Contains(sourceExtension) && group.Contains(target);
    }

    public static IReadOnlyList<string> TargetsFor(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return [];
        }
        var extensions = paths.Select(ExtensionOf).ToList();
        var targetLists = extensions.Select(AllTargetsFor).ToList();
        var shared = targetLists[0].Where(target => targetLists.All(list => list.Contains(target)));
        return shared.Where(target => !extensions.All(extension => extension == target)).ToList();
    }

    private static List<string> AllTargetsFor(string extension)
    {
        var category = CategoryOf(extension);
        if (category is null)
        {
            return [];
        }
        var candidates = new List<string>();
        if (Aliases.TryGetValue(extension, out var alias))
        {
            candidates.Add(alias);
        }
        candidates.AddRange(category.Targets);
        candidates.Add(extension);
        return candidates.Distinct().ToList();
    }

    private static IReadOnlyList<FormatCategory> CreateCategories()
    {
        return
        [
            new FormatCategory(
                "image",
                "Фото и картинки",
                "#0F8F6F",
                "#FFFFFF",
                ["jpg", "jpeg", "png", "webp", "heic", "avif", "bmp", "tiff", "tif", "ico"],
                ["png", "jpg", "webp", "svg", "pdf", "ico", "bmp", "gif", "tiff", "avif"]),
            new FormatCategory(
                "vector",
                "Вектор",
                "#0B8AA0",
                "#FFFFFF",
                ["svg"],
                ["png", "jpg", "webp", "pdf", "ico"]),
            new FormatCategory(
                "animation",
                "GIF-анимация",
                "#B23D8D",
                "#FFFFFF",
                ["gif"],
                ["mp4", "webm", "webp", "png", "jpg"]),
            new FormatCategory(
                "video",
                "Видео",
                "#6F4FE8",
                "#FFFFFF",
                ["mp4", "mov", "mkv", "avi", "webm", "wmv", "m4v"],
                ["mp4", "mov", "mkv", "webm", "gif", "mp3", "avi", "wav", "m4a"])
            {
                StreamCopyGroup = ["mp4", "mov", "mkv", "m4v"],
            },
            new FormatCategory(
                "audio",
                "Звук",
                "#E8971A",
                "#201300",
                ["mp3", "wav", "flac", "ogg", "m4a", "aac", "opus", "wma"],
                ["mp3", "wav", "flac", "ogg", "m4a", "opus", "aac"]),
            new FormatCategory(
                "text",
                "Тексты",
                "#2F6FE0",
                "#FFFFFF",
                ["docx", "doc", "odt", "rtf", "txt", "md", "html"],
                ["pdf", "docx", "txt", "rtf", "odt", "doc", "html", "md"]),
            new FormatCategory(
                "pdf",
                "PDF",
                "#D63F35",
                "#FFFFFF",
                ["pdf"],
                ["docx", "txt", "png", "jpg"])
            {
                FolderTargets = ["png", "jpg"],
            },
            new FormatCategory(
                "slides",
                "Презентации",
                "#D0571E",
                "#FFFFFF",
                ["pptx", "ppt", "odp"],
                ["pdf", "pptx", "ppt", "odp", "mp4", "png", "jpg"])
            {
                FolderTargets = ["png", "jpg"],
            },
            new FormatCategory(
                "sheet",
                "Таблицы",
                "#1A8A4C",
                "#FFFFFF",
                ["xlsx", "xls", "ods", "csv"],
                ["xlsx", "csv", "pdf", "xls", "ods", "txt"]),
        ];
    }
}
