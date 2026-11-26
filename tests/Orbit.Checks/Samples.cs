using System.Text;
using ImageMagick;
using ImageMagick.Drawing;
using Orbit.Core.Office;
using Orbit.Core.Tools;

namespace Orbit.Checks;

internal sealed class Samples(string folder)
{
    public string Png => Path.Combine(folder, "картинка.png");
    public string Jpg => Path.Combine(folder, "фото.jpg");
    public string Svg => Path.Combine(folder, "логотип.svg");
    public string Markdown => Path.Combine(folder, "заметка.md");
    public string Text1251 => Path.Combine(folder, "старый текст.txt");
    public string Html => Path.Combine(folder, "страница.html");
    public string Csv => Path.Combine(folder, "таблица.csv");
    public string Wav => Path.Combine(folder, "звук.wav");
    public string Mp4 => Path.Combine(folder, "ролик.mp4");
    public string Gif => Path.Combine(folder, "анимация.gif");
    public string Pptx => Path.Combine(folder, "доклад.pptx");

    public async Task CreateAsync()
    {
        Directory.CreateDirectory(folder);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CreateImages();
        File.WriteAllText(Svg, SvgText, new UTF8Encoding(false));
        File.WriteAllText(Markdown, MarkdownText, new UTF8Encoding(false));
        File.WriteAllText(Text1251, "Привет из Windows-1251.\r\n\r\nВторой абзац, ёлка и щука.", Encoding.GetEncoding(1251));
        File.WriteAllText(Html, HtmlText, new UTF8Encoding(false));
        File.WriteAllText(Csv, "Имя;Количество\nЯблоки;3\nГруши;5\n", new UTF8Encoding(false));
        await CreateMediaAsync();
        await CreatePresentationAsync();
    }

    private void CreateImages()
    {
        using var image = new MagickImage(MagickColors.Transparent, 400, 300);
        new Drawables()
            .FillColor(MagickColors.OrangeRed)
            .Circle(140, 150, 140, 60)
            .FillColor(MagickColors.SteelBlue)
            .Rectangle(220, 80, 360, 220)
            .Draw(image);
        image.Write(Png, MagickFormat.Png);
        using var photo = new MagickImage(Png);
        photo.BackgroundColor = MagickColors.White;
        photo.Alpha(AlphaAction.Remove);
        photo.Write(Jpg, MagickFormat.Jpeg);
    }

    private async Task CreateMediaAsync()
    {
        var ffmpeg = ToolLocator.Ffmpeg ?? throw new InvalidOperationException("FFmpeg не найден");
        await RunFfmpegAsync(ffmpeg, ["-f", "lavfi", "-i", "sine=frequency=440:duration=2", Wav]);
        await RunFfmpegAsync(ffmpeg,
        [
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=330:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", Mp4,
        ]);
        await RunFfmpegAsync(ffmpeg, ["-f", "lavfi", "-i", "testsrc2=size=160x120:rate=10:duration=1", Gif]);
    }

    private static async Task RunFfmpegAsync(string ffmpeg, string[] arguments)
    {
        var outcome = await ExternalProcess.RunAsync(ffmpeg, ["-hide_banner", "-y", .. arguments], null, CancellationToken.None);
        if (outcome.ExitCode != 0)
        {
            throw new InvalidOperationException($"Образец не создался: {outcome.ErrorTail}");
        }
    }

    private Task CreatePresentationAsync()
    {
        var path = Pptx;
        return OfficeHost.PowerPoint.RunAsync(application =>
        {
            dynamic powerPoint = application;
            var presentation = powerPoint.Presentations.Add(0);
            try
            {
                for (var number = 1; number <= 2; number++)
                {
                    var slide = presentation.Slides.Add(number, 1);
                    slide.Shapes[1].TextFrame.TextRange.Text = $"Слайд {number}";
                    slide.Shapes[2].TextFrame.TextRange.Text = "Проверка Orbit";
                }
                presentation.SaveAs(path, 24);
            }
            finally
            {
                presentation.Close();
            }
        }, CancellationToken.None);
    }

    private const string SvgText =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"80\" viewBox=\"0 0 120 80\">"
        + "<rect x=\"4\" y=\"4\" width=\"112\" height=\"72\" rx=\"14\" fill=\"#2F6FE0\"/>"
        + "<circle cx=\"60\" cy=\"40\" r=\"22\" fill=\"#FFFFFF\"/></svg>";

    private const string MarkdownText =
        "# Заголовок\n\nТекст **жирный** и *курсив*.\n\n- раз\n- два\n\n| Имя | Число |\n|---|---|\n| Яблоки | 3 |\n";

    private const string HtmlText =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Страница</title></head>"
        + "<body><h1>Привет</h1><p>Это <b>проверка</b> Orbit.</p><ul><li>раз</li><li>два</li></ul></body></html>";
}
