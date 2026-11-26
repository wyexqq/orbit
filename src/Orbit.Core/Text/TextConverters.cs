using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Orbit.Core.Conversion;

namespace Orbit.Core.Text;

public static class HtmlPage
{
    private const string Style =
        "body{font-family:'Segoe UI',Arial,sans-serif;max-width:820px;margin:40px auto;padding:0 20px;line-height:1.6;color:#1A1D23}"
        + "pre,code{font-family:Consolas,monospace;background:#F2F3F5;border-radius:4px}"
        + "pre{padding:12px;overflow:auto}table{border-collapse:collapse}td,th{border:1px solid #D5D8DE;padding:4px 8px}"
        + "img{max-width:100%}";

    public static string Wrap(string title, string body)
    {
        var page = new StringBuilder();
        page.AppendLine("<!DOCTYPE html>");
        page.AppendLine("<html>");
        page.AppendLine("<head>");
        page.AppendLine("<meta charset=\"utf-8\">");
        page.AppendLine($"<title>{WebUtility.HtmlEncode(title)}</title>");
        page.AppendLine($"<style>{Style}</style>");
        page.AppendLine("</head>");
        page.AppendLine("<body>");
        page.AppendLine(body);
        page.AppendLine("</body>");
        page.AppendLine("</html>");
        return page.ToString();
    }
}

public sealed class MarkdownToHtmlConverter : IFileConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() =>
        {
            var markdown = TextEncoding.ReadAllText(work.SourcePath);
            var body = Markdown.ToHtml(markdown, Pipeline);
            var title = Path.GetFileNameWithoutExtension(work.SourcePath);
            TextEncoding.WriteAllText(work.OutputPath, HtmlPage.Wrap(title, body));
            work.Progress.Report(1);
        }, cancellation);
    }
}

public sealed class PlainTextToHtmlConverter : IFileConverter
{
    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() =>
        {
            var text = TextEncoding.ReadAllText(work.SourcePath).Replace("\r\n", "\n");
            var paragraphs = text
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(paragraph => $"<p>{WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>\n")}</p>");
            var title = Path.GetFileNameWithoutExtension(work.SourcePath);
            TextEncoding.WriteAllText(work.OutputPath, HtmlPage.Wrap(title, string.Join("\n", paragraphs)));
            work.Progress.Report(1);
        }, cancellation);
    }
}

public sealed class HtmlToMarkdownConverter : IFileConverter
{
    private static readonly ReverseMarkdown.Converter Converter = new(new ReverseMarkdown.Config
    {
        GithubFlavored = true,
        Formatting = { RemoveComments = true },
        Links = { SmartHref = true },
        Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass },
    });

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return Task.Run(() =>
        {
            var html = BodyOf(TextEncoding.ReadAllText(work.SourcePath));
            string markdown;
            lock (Converter)
            {
                markdown = Converter.Convert(html);
            }
            TextEncoding.WriteAllText(work.OutputPath, markdown.Trim() + Environment.NewLine);
            work.Progress.Report(1);
        }, cancellation);
    }

    private static string BodyOf(string html)
    {
        var body = Regex.Match(html, "<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return body.Success ? body.Groups[1].Value : html;
    }
}
