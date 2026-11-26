namespace Orbit.Core.Formats;

public static class FormatCatalog
{
    public const string RenameHint = "Тот же формат — просто переименую";
    public const string StreamCopyHint = "Без перекодирования — почти мгновенно";

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

    public static string HintFor(string sourceExtension, string target)
    {
        if (IsRename(sourceExtension, target))
        {
            return RenameHint;
        }
        if (CanStreamCopy(sourceExtension, target))
        {
            return StreamCopyHint;
        }
        var category = CategoryOf(sourceExtension);
        return category?.Hints.GetValueOrDefault(target) ?? string.Empty;
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
                ["png", "jpg", "webp", "svg", "pdf", "ico", "bmp", "gif", "tiff", "avif"],
                new Dictionary<string, string>
                {
                    ["png"] = "Без потерь, с прозрачностью",
                    ["jpg"] = "Легче, но без прозрачности",
                    ["webp"] = "Лёгкий формат для сайтов",
                    ["svg"] = "Обвод в вектор — для логотипов и иконок",
                    ["pdf"] = "Картинка на страницу PDF",
                    ["ico"] = "Иконка 16–256 px",
                    ["bmp"] = "Без сжатия",
                    ["gif"] = "Не больше 256 цветов",
                    ["tiff"] = "Для печати",
                    ["avif"] = "Самый лёгкий, открывается не везде",
                }),
            new FormatCategory(
                "vector",
                "Вектор",
                "#0B8AA0",
                "#FFFFFF",
                ["svg"],
                ["png", "jpg", "webp", "pdf", "ico"],
                new Dictionary<string, string>
                {
                    ["png"] = "Отрисую в 2048 px с прозрачностью",
                    ["jpg"] = "Отрисую на белом фоне",
                    ["webp"] = "Отрисую для сайта",
                    ["pdf"] = "Останется векторным",
                    ["ico"] = "Иконка 16–256 px",
                }),
            new FormatCategory(
                "animation",
                "GIF-анимация",
                "#B23D8D",
                "#FFFFFF",
                ["gif"],
                ["mp4", "webm", "webp", "png", "jpg"],
                new Dictionary<string, string>
                {
                    ["mp4"] = "Видео, в разы легче",
                    ["webm"] = "Видео для сайтов",
                    ["webp"] = "Анимация, легче GIF",
                    ["png"] = "Первый кадр",
                    ["jpg"] = "Первый кадр",
                }),
            new FormatCategory(
                "video",
                "Видео",
                "#6F4FE8",
                "#FFFFFF",
                ["mp4", "mov", "mkv", "avi", "webm", "wmv", "m4v"],
                ["mp4", "mov", "mkv", "webm", "gif", "mp3", "avi", "wav", "m4a"],
                new Dictionary<string, string>
                {
                    ["mp4"] = "Открывается везде",
                    ["mov"] = "Для Apple и монтажа",
                    ["mkv"] = "Контейнер без ограничений",
                    ["webm"] = "Для сайтов",
                    ["gif"] = "Анимация без звука",
                    ["mp3"] = "Только звук",
                    ["avi"] = "Старый формат",
                    ["wav"] = "Звук без сжатия",
                    ["m4a"] = "Звук в AAC",
                })
            {
                StreamCopyGroup = ["mp4", "mov", "mkv", "m4v"],
            },
            new FormatCategory(
                "audio",
                "Звук",
                "#E8971A",
                "#201300",
                ["mp3", "wav", "flac", "ogg", "m4a", "aac", "opus", "wma"],
                ["mp3", "wav", "flac", "ogg", "m4a", "opus", "aac"],
                new Dictionary<string, string>
                {
                    ["mp3"] = "Открывается везде, 320 кбит/с",
                    ["wav"] = "Без сжатия",
                    ["flac"] = "Без потерь, легче WAV",
                    ["ogg"] = "Открытый формат",
                    ["m4a"] = "AAC, для Apple",
                    ["opus"] = "Лучшее сжатие, хорош для голоса",
                    ["aac"] = "Как M4A, без контейнера",
                }),
            new FormatCategory(
                "text",
                "Тексты",
                "#2F6FE0",
                "#FFFFFF",
                ["docx", "doc", "odt", "rtf", "txt", "md", "html"],
                ["pdf", "docx", "txt", "rtf", "odt", "doc", "html", "md"],
                new Dictionary<string, string>
                {
                    ["pdf"] = "Сохраню через Word",
                    ["docx"] = "Обычный Word",
                    ["txt"] = "Только текст, без оформления",
                    ["rtf"] = "Простой формат с оформлением",
                    ["odt"] = "Для LibreOffice",
                    ["doc"] = "Word 97–2003",
                    ["html"] = "Веб-страница",
                    ["md"] = "Разметка Markdown",
                }),
            new FormatCategory(
                "pdf",
                "PDF",
                "#D63F35",
                "#FFFFFF",
                ["pdf"],
                ["docx", "txt", "png", "jpg"],
                new Dictionary<string, string>
                {
                    ["docx"] = "Word восстановит вёрстку",
                    ["txt"] = "Только текст",
                    ["png"] = "Папка: картинка на каждую страницу",
                    ["jpg"] = "Папка: картинка на каждую страницу",
                })
            {
                FolderTargets = ["png", "jpg"],
            },
            new FormatCategory(
                "slides",
                "Презентации",
                "#D0571E",
                "#FFFFFF",
                ["pptx", "ppt", "odp"],
                ["pdf", "pptx", "ppt", "odp", "mp4", "png", "jpg"],
                new Dictionary<string, string>
                {
                    ["pdf"] = "Сохраню через PowerPoint",
                    ["pptx"] = "Новый формат PowerPoint",
                    ["ppt"] = "PowerPoint 97–2003",
                    ["odp"] = "Для LibreOffice",
                    ["mp4"] = "Видео из слайдов",
                    ["png"] = "Папка: картинка на каждый слайд",
                    ["jpg"] = "Папка: картинка на каждый слайд",
                })
            {
                FolderTargets = ["png", "jpg"],
            },
            new FormatCategory(
                "sheet",
                "Таблицы",
                "#1A8A4C",
                "#FFFFFF",
                ["xlsx", "xls", "ods", "csv"],
                ["xlsx", "csv", "pdf", "xls", "ods", "txt"],
                new Dictionary<string, string>
                {
                    ["xlsx"] = "Обычный Excel",
                    ["csv"] = "Открытый лист, текстом",
                    ["pdf"] = "Сохраню через Excel",
                    ["xls"] = "Excel 97–2003",
                    ["ods"] = "Для LibreOffice",
                    ["txt"] = "Текст с табуляцией",
                }),
        ];
    }
}
