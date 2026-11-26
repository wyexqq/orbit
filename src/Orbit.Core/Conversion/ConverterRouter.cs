using Orbit.Core.Formats;
using Orbit.Core.Office;
using Orbit.Core.Text;

namespace Orbit.Core.Conversion;

public static class ConverterRouter
{
    private static readonly IFileConverter Images = new ImageConverter();
    private static readonly IFileConverter Tracer = new VectorTracer();
    private static readonly IFileConverter Svg = new SvgConverter();
    private static readonly IFileConverter Ffmpeg = new FfmpegConverter();
    private static readonly IFileConverter PdfPages = new PdfPagesConverter();
    private static readonly IFileConverter Word = new WordConverter();
    private static readonly IFileConverter PowerPoint = new PowerPointConverter();
    private static readonly IFileConverter Excel = new ExcelConverter();
    private static readonly IFileConverter MarkdownToHtml = new MarkdownToHtmlConverter();
    private static readonly IFileConverter PlainTextToHtml = new PlainTextToHtmlConverter();
    private static readonly IFileConverter HtmlToMarkdown = new HtmlToMarkdownConverter();

    private static readonly IFileConverter MarkdownThroughWord =
        new ChainedConverter([new ChainStep(MarkdownToHtml, "html")], Word);

    private static readonly IFileConverter WordToMarkdown =
        new ChainedConverter([new ChainStep(Word, "html")], HtmlToMarkdown);

    public static IFileConverter For(string sourceExtension, string target)
    {
        var categoryId = FormatCatalog.CategoryOf(sourceExtension)?.Id;
        return categoryId switch
        {
            "image" => target == "svg" ? Tracer : Images,
            "vector" => Svg,
            "animation" => target is "mp4" or "webm" ? Ffmpeg : Images,
            "video" or "audio" => Ffmpeg,
            "text" => ForText(sourceExtension, target),
            "pdf" => target is "png" or "jpg" ? PdfPages : Word,
            "slides" => PowerPoint,
            "sheet" => Excel,
            _ => throw new NotSupportedException($"Не умею работать с .{sourceExtension}"),
        };
    }

    private static IFileConverter ForText(string sourceExtension, string target)
    {
        if (sourceExtension == "md")
        {
            return target == "html" ? MarkdownToHtml : MarkdownThroughWord;
        }
        if (target == "md")
        {
            return sourceExtension == "html" ? HtmlToMarkdown : WordToMarkdown;
        }
        if (sourceExtension == "txt" && target == "html")
        {
            return PlainTextToHtml;
        }
        return Word;
    }
}
