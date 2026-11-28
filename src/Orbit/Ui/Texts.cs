using Orbit.Core.Conversion;
using Orbit.Core.Formats;

namespace Orbit.Ui;

internal static class Texts
{
    public static string Files(int count)
    {
        var lastTwoDigits = count % 100;
        var lastDigit = count % 10;
        if (lastTwoDigits is >= 11 and <= 14)
        {
            return "файлов";
        }
        if (lastDigit == 1)
        {
            return "файл";
        }
        return lastDigit is >= 2 and <= 4 ? "файла" : "файлов";
    }

    public static string Seconds(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }

    public static string ProgressTitle(ConversionRequest request)
    {
        var target = request.Target.ToUpperInvariant();
        if (request.Paths.Count == 1)
        {
            return $"{Path.GetFileName(request.Paths[0])} → {target}";
        }
        return $"{request.Paths.Count} {Files(request.Paths.Count)} → {target}";
    }

    public static string ProgressLine(RunProgress progress)
    {
        var percent = (int)Math.Round(progress.Overall * 100);
        if (progress.FileCount > 1)
        {
            return $"{percent}% · {progress.FileIndex + 1} из {progress.FileCount} · идёт в фоне";
        }
        return $"{percent}% · идёт в фоне";
    }

    public static string DoneTitle(ConversionResult result)
    {
        var done = result.Outcomes.Where(outcome => outcome.Succeeded).ToList();
        if (done.Count == 1)
        {
            return $"{Path.GetFileName(done[0].SourcePath)} → {Path.GetFileName(done[0].OutputPath)}";
        }
        return $"{done.Count} {Files(done.Count)} → {result.Request.Target.ToUpperInvariant()}";
    }

    public static string DoneSubtitle(ConversionResult result, TimeSpan elapsed)
    {
        var done = result.Outcomes.Where(outcome => outcome.Succeeded).ToList();
        var timing = $"Готово за {Seconds(elapsed)} с";
        var failedCount = result.Outcomes.Count - done.Count;
        var failures = failedCount > 0 ? $" · не вышло: {failedCount}" : string.Empty;
        if (done.All(outcome => outcome.Fate == OriginalFate.Renamed))
        {
            return "Внутри тот же файл, сменил только расширение" + failures;
        }
        if (done.Any(outcome => FormatCatalog.ProducesFolder(FormatCatalog.ExtensionOf(outcome.SourcePath), result.Request.Target)))
        {
            return $"{timing} · исходник на месте{failures}";
        }
        if (done.Any(outcome => outcome.Fate == OriginalFate.KeptOnRemovableDrive))
        {
            return $"{timing} · оригинал оставил: на этом диске нет Корзины{failures}";
        }
        if (done.All(outcome => outcome.Fate == OriginalFate.Kept))
        {
            return $"{timing} · оригинал остался рядом{failures}";
        }
        return $"{timing} · старый файл в Корзине{failures}";
    }

    public static string FailureTitle(ConversionResult result)
    {
        var failure = result.Failures.First();
        return result.Outcomes.Count == 1
            ? $"Не получилось: {Path.GetFileName(failure.SourcePath)}"
            : $"Не получилось ни с одним из {result.Outcomes.Count} {Files(result.Outcomes.Count)}";
    }
}
