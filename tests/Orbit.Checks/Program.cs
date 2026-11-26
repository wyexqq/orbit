using System.Diagnostics;
using Orbit.Checks;
using Orbit.Core.Conversion;
using Orbit.Core.Files;
using Orbit.Core.Office;
using Orbit.Core.Text;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var workFolder = Path.Combine(Path.GetTempPath(), $"orbit-checks-{DateTime.Now:HHmmss}");
var samples = new Samples(workFolder);
var report = new CheckReport(args.FirstOrDefault());

try
{
    Console.WriteLine($"Папка проверки: {workFolder}");
    await samples.CreateAsync();

    await report.Convert(samples.Png, "jpg");
    await report.Convert(samples.Png, "webp");
    await report.Convert(samples.Png, "ico");
    await report.Convert(samples.Png, "gif");
    await report.Convert(samples.Png, "bmp");
    await report.Convert(samples.Png, "tiff");
    await report.Convert(samples.Png, "avif");
    await report.Convert(samples.Png, "pdf");
    await report.Convert(samples.Png, "svg", output => File.ReadAllText(output).Contains("<path"));
    await report.Convert(samples.Jpg, "jpeg");

    await report.Convert(samples.Svg, "png");
    await report.Convert(samples.Svg, "jpg");
    await report.Convert(samples.Svg, "pdf");
    await report.Convert(samples.Svg, "ico");

    await report.Convert(samples.Gif, "mp4");
    await report.Convert(samples.Gif, "webp");
    await report.Convert(samples.Gif, "png");

    await report.Convert(samples.Mp4, "mkv");
    await report.Convert(samples.Mp4, "mov");
    await report.Convert(samples.Mp4, "webm");
    await report.Convert(samples.Mp4, "gif");
    await report.Convert(samples.Mp4, "mp3");
    await report.Convert(samples.Mp4, "wav");

    await report.Convert(samples.Wav, "mp3");
    await report.Convert(samples.Wav, "flac");
    await report.Convert(samples.Wav, "ogg");
    await report.Convert(samples.Wav, "opus");
    await report.Convert(samples.Wav, "m4a");

    await report.Convert(samples.Markdown, "html", output => File.ReadAllText(output).Contains("<h1"));
    await report.Convert(samples.Text1251, "html", output => File.ReadAllText(output).Contains("ёлка"));
    await report.Convert(samples.Html, "md", output => File.ReadAllText(output).Contains("# Привет"));
    var docx = await report.Convert(samples.Html, "docx");
    await report.Convert(samples.Markdown, "docx");
    await report.Convert(samples.Text1251, "docx");
    if (docx is not null)
    {
        await report.Convert(docx, "txt", output => TextEncoding.ReadAllText(output).Contains("Привет"));
        await report.Convert(docx, "md", output => File.ReadAllText(output).Contains("Привет"));
        await report.Convert(docx, "rtf");
        await report.Convert(docx, "odt");
        var pdf = await report.Convert(docx, "pdf");
        if (pdf is not null)
        {
            await report.Convert(pdf, "png", output => Directory.EnumerateFiles(output).Any());
            await report.Convert(pdf, "txt", output => TextEncoding.ReadAllText(output).Contains("Привет"));
        }
    }

    var xlsx = await report.Convert(samples.Csv, "xlsx");
    if (xlsx is not null)
    {
        await report.Convert(xlsx, "csv", output => TextEncoding.ReadAllText(output).Contains("Яблоки"));
        await report.Convert(xlsx, "pdf");
        await report.Convert(xlsx, "ods");
    }

    await report.Convert(samples.Pptx, "pdf");
    await report.Convert(samples.Pptx, "png", output => Directory.EnumerateFiles(output).Count() == 2);
    await report.Convert(samples.Pptx, "mp4");

    await report.CheckRecycleAndUndo(samples.Png);
    await report.CheckRenameAndUndo(samples.Markdown);
    report.CheckNoWorkFoldersLeft(workFolder);
}
catch (Exception error)
{
    Console.WriteLine($"Проверка оборвалась: {error}");
    report.MarkBroken();
}
finally
{
    OfficeHost.QuitAllIfIdle(TimeSpan.FromSeconds(10));
}

report.PrintSummary();
if (report.AllPassed)
{
    Directory.Delete(workFolder, recursive: true);
}
return report.AllPassed ? 0 : 1;

internal sealed class CheckReport(string? onlyMatching)
{
    private int passed;
    private int failed;

    public bool AllPassed => failed == 0;

    public void MarkBroken()
    {
        failed += 1;
    }

    public async Task<string?> Convert(string source, string target, Func<string, bool>? verify = null)
    {
        var label = $"{Path.GetFileName(source)} → {target}";
        if (onlyMatching is not null && !label.Contains(onlyMatching, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var stopwatch = Stopwatch.StartNew();
        var request = new ConversionRequest([source], target, KeepOriginals: true);
        var result = await ConversionRunner.RunAsync(request, new SyncProgress<RunProgress>(_ => { }), CancellationToken.None);
        var outcome = result.Outcomes.Single();
        var seconds = stopwatch.Elapsed.TotalSeconds;
        if (!outcome.Succeeded)
        {
            Fail(label, outcome.Error ?? "без ошибки", seconds);
            return null;
        }
        var output = outcome.OutputPath!;
        if (File.GetAttributes(output).HasFlag(FileAttributes.Hidden))
        {
            Fail(label, "результат остался скрытым", seconds);
            return output;
        }
        if (verify is not null && !verify(output))
        {
            Fail(label, "результат не прошёл проверку содержимого", seconds);
            return output;
        }
        Pass(label, $"{SizeOf(output)}", seconds);
        return output;
    }

    public async Task CheckRecycleAndUndo(string png)
    {
        const string label = "Корзина и отмена";
        var source = Path.Combine(Path.GetDirectoryName(png)!, "в корзину.png");
        File.Copy(png, source);
        if (!RecycleBin.IsAvailableFor(source))
        {
            Pass(label, "пропуск: на этом диске нет Корзины", 0);
            return;
        }
        var request = new ConversionRequest([source], "jpg", KeepOriginals: false);
        var result = await ConversionRunner.RunAsync(request, new SyncProgress<RunProgress>(_ => { }), CancellationToken.None);
        var outcome = result.Outcomes.Single();
        if (outcome.Fate != OriginalFate.Recycled || File.Exists(source))
        {
            Fail(label, $"оригинал не ушёл в Корзину ({outcome.Fate}, {outcome.Error})", 0);
            return;
        }
        var problems = ConversionRunner.Undo(result);
        var restored = File.Exists(source) && !File.Exists(outcome.OutputPath);
        if (problems.Count > 0 || !restored)
        {
            Fail(label, $"отмена не вернула файл: {string.Join(", ", problems)}", 0);
            return;
        }
        Pass(label, "ушёл в Корзину и вернулся", 0);
    }

    public async Task CheckRenameAndUndo(string markdown)
    {
        const string label = "Переименование md → txt и отмена";
        var source = Path.Combine(Path.GetDirectoryName(markdown)!, "переименовать.md");
        File.Copy(markdown, source);
        var request = new ConversionRequest([source], "txt", KeepOriginals: false);
        var result = await ConversionRunner.RunAsync(request, new SyncProgress<RunProgress>(_ => { }), CancellationToken.None);
        var outcome = result.Outcomes.Single();
        if (outcome.Fate != OriginalFate.Renamed || File.Exists(source) || !File.Exists(outcome.OutputPath))
        {
            Fail(label, $"не переименовалось ({outcome.Fate}, {outcome.Error})", 0);
            return;
        }
        ConversionRunner.Undo(result);
        if (!File.Exists(source) || File.Exists(outcome.OutputPath))
        {
            Fail(label, "отмена не вернула имя", 0);
            return;
        }
        Pass(label, "переименовалось и вернулось", 0);
    }

    public void CheckNoWorkFoldersLeft(string folder)
    {
        const string label = "Временные папки убраны";
        var leftovers = Directory.EnumerateDirectories(folder, ".orbit-*").Select(Path.GetFileName).ToList();
        if (leftovers.Count > 0)
        {
            Fail(label, $"остались: {string.Join(", ", leftovers)}", 0);
            return;
        }
        Pass(label, "ничего не осталось", 0);
    }

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine($"Итого: прошло {passed}, не прошло {failed}");
    }

    private void Pass(string label, string detail, double seconds)
    {
        passed += 1;
        Console.WriteLine($"  ок   {label,-34} {detail,-28} {seconds,6:0.00} с");
    }

    private void Fail(string label, string reason, double seconds)
    {
        failed += 1;
        Console.WriteLine($"  СБОЙ {label,-34} {reason} ({seconds:0.00} с)");
    }

    private static string SizeOf(string path)
    {
        if (Directory.Exists(path))
        {
            return $"папка, файлов: {Directory.EnumerateFiles(path).Count()}";
        }
        var bytes = new FileInfo(path).Length;
        return bytes < 1024 ? $"{bytes} Б" : $"{bytes / 1024.0:0.0} КБ";
    }
}
