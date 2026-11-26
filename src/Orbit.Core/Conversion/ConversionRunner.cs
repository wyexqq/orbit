using Orbit.Core.Files;
using Orbit.Core.Formats;

namespace Orbit.Core.Conversion;

public sealed record ConversionRequest(IReadOnlyList<string> Paths, string Target, bool KeepOriginals);

public enum OriginalFate
{
    Kept,
    Recycled,
    Renamed,
    KeptOnRemovableDrive,
}

public sealed record FileOutcome(string SourcePath, string? OutputPath, OriginalFate Fate, string? Error)
{
    public bool Succeeded => Error is null;
}

public enum UndoKind
{
    DeleteOutput,
    RestoreFromRecycleBin,
    MoveBack,
}

public sealed record UndoStep(UndoKind Kind, string Path, string? OriginalPath = null);

public sealed record RunProgress(int FileIndex, int FileCount, string FileName, double FileFraction)
{
    public double Overall => (FileIndex + Math.Clamp(FileFraction, 0, 1)) / FileCount;
}

public sealed record ConversionResult(
    ConversionRequest Request,
    IReadOnlyList<FileOutcome> Outcomes,
    IReadOnlyList<UndoStep> UndoSteps,
    bool WasCancelled)
{
    public int SucceededCount => Outcomes.Count(outcome => outcome.Succeeded);

    public IEnumerable<FileOutcome> Failures => Outcomes.Where(outcome => !outcome.Succeeded);
}

public static class ConversionRunner
{
    public static async Task<ConversionResult> RunAsync(
        ConversionRequest request,
        IProgress<RunProgress> progress,
        CancellationToken cancellation)
    {
        var outcomes = new List<FileOutcome>();
        var undoSteps = new List<UndoStep>();
        var wasCancelled = false;
        for (var index = 0; index < request.Paths.Count; index++)
        {
            var sourcePath = request.Paths[index];
            var fileIndex = index;
            var fileName = Path.GetFileName(sourcePath);
            var fileProgress = new SyncProgress<double>(
                fraction => progress.Report(new RunProgress(fileIndex, request.Paths.Count, fileName, fraction)));
            fileProgress.Report(0);
            try
            {
                var (outcome, steps) = await ConvertOneAsync(sourcePath, request, fileProgress, cancellation);
                outcomes.Add(outcome);
                undoSteps.AddRange(steps);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                break;
            }
            catch (Exception error)
            {
                Log.Error($"Не получилось {fileName} → {request.Target}", error);
                outcomes.Add(new FileOutcome(sourcePath, null, OriginalFate.Kept, error.Message));
            }
        }
        return new ConversionResult(request, outcomes, undoSteps, wasCancelled);
    }

    public static IReadOnlyList<string> Undo(ConversionResult result)
    {
        var problems = new List<string>();
        foreach (var step in result.UndoSteps.Reverse())
        {
            try
            {
                ApplyUndo(step);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Не отменилось: {step}", error);
                problems.Add(Path.GetFileName(step.Path));
            }
        }
        return problems;
    }

    private static async Task<(FileOutcome Outcome, List<UndoStep> Steps)> ConvertOneAsync(
        string sourcePath,
        ConversionRequest request,
        IProgress<double> progress,
        CancellationToken cancellation)
    {
        var sourceExtension = FormatCatalog.ExtensionOf(sourcePath);
        var directory = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (FormatCatalog.IsRename(sourceExtension, request.Target))
        {
            return Rename(sourcePath, directory, baseName, request);
        }
        var producesFolder = FormatCatalog.ProducesFolder(sourceExtension, request.Target);
        var temporaryOutput = TemporaryFiles.NewSiblingPath(sourcePath, producesFolder ? string.Empty : request.Target);
        try
        {
            var converter = ConverterRouter.For(sourceExtension, request.Target);
            await converter.ConvertAsync(new ConversionWork(sourcePath, request.Target, temporaryOutput, progress), cancellation);
            cancellation.ThrowIfCancellationRequested();
            EnsureProduced(temporaryOutput, producesFolder);
            var published = producesFolder
                ? PublishFolder(sourcePath, temporaryOutput, directory, baseName, request.Target)
                : PublishFile(sourcePath, temporaryOutput, directory, baseName, request);
            progress.Report(1);
            return published;
        }
        finally
        {
            TemporaryFiles.DeleteWorkFolder(temporaryOutput);
        }
    }

    private static (FileOutcome Outcome, List<UndoStep> Steps) PublishFolder(
        string sourcePath,
        string temporaryOutput,
        string directory,
        string baseName,
        string target)
    {
        var finalFolder = UniquePaths.ForFolder(directory, $"{baseName} — {target.ToUpperInvariant()}");
        Directory.Move(temporaryOutput, finalFolder);
        ShellNotify.Added(finalFolder);
        var outcome = new FileOutcome(sourcePath, finalFolder, OriginalFate.Kept, null);
        return (outcome, [new UndoStep(UndoKind.DeleteOutput, finalFolder)]);
    }

    private static (FileOutcome Outcome, List<UndoStep> Steps) PublishFile(
        string sourcePath,
        string temporaryOutput,
        string directory,
        string baseName,
        ConversionRequest request)
    {
        var finalFile = UniquePaths.ForFile(directory, baseName, request.Target);
        File.Move(temporaryOutput, finalFile);
        ShellNotify.Added(finalFile);
        MoveCompanionFolder(temporaryOutput, finalFile);
        var steps = new List<UndoStep> { new(UndoKind.DeleteOutput, finalFile) };
        var fate = DisposeOfOriginal(sourcePath, request.KeepOriginals, steps);
        return (new FileOutcome(sourcePath, finalFile, fate, null), steps);
    }

    private static (FileOutcome Outcome, List<UndoStep> Steps) Rename(
        string sourcePath,
        string directory,
        string baseName,
        ConversionRequest request)
    {
        var finalPath = UniquePaths.ForFile(directory, baseName, request.Target);
        if (request.KeepOriginals)
        {
            File.Copy(sourcePath, finalPath);
            ShellNotify.Added(finalPath);
            return (new FileOutcome(sourcePath, finalPath, OriginalFate.Kept, null), [new UndoStep(UndoKind.DeleteOutput, finalPath)]);
        }
        File.Move(sourcePath, finalPath);
        ShellNotify.Renamed(sourcePath, finalPath);
        return (new FileOutcome(sourcePath, finalPath, OriginalFate.Renamed, null), [new UndoStep(UndoKind.MoveBack, finalPath, sourcePath)]);
    }

    private static OriginalFate DisposeOfOriginal(string sourcePath, bool keepOriginal, List<UndoStep> steps)
    {
        if (keepOriginal)
        {
            return OriginalFate.Kept;
        }
        if (!RecycleBin.IsAvailableFor(sourcePath))
        {
            return OriginalFate.KeptOnRemovableDrive;
        }
        try
        {
            RecycleBin.Send(sourcePath);
            steps.Add(new UndoStep(UndoKind.RestoreFromRecycleBin, sourcePath));
            return OriginalFate.Recycled;
        }
        catch (IOException error)
        {
            Log.Error("Оригинал остался на месте", error);
            return OriginalFate.Kept;
        }
    }

    private static void EnsureProduced(string outputPath, bool producesFolder)
    {
        var produced = producesFolder
            ? Directory.Exists(outputPath) && Directory.EnumerateFiles(outputPath).Any()
            : File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        if (!produced)
        {
            throw new InvalidOperationException("Результат получился пустым");
        }
    }

    private static void MoveCompanionFolder(string temporaryOutput, string finalFile)
    {
        var temporaryFolder = CompanionFolderOf(temporaryOutput);
        if (FormatCatalog.ExtensionOf(finalFile) != "html" || !Directory.Exists(temporaryFolder))
        {
            return;
        }
        var finalFolder = UniquePaths.ForFolder(Path.GetDirectoryName(finalFile)!, Path.GetFileName(CompanionFolderOf(finalFile)));
        Directory.Move(temporaryFolder, finalFolder);
        ShellNotify.Added(finalFolder);
        var html = File.ReadAllText(finalFile);
        var oldReference = Path.GetFileName(temporaryFolder) + "/";
        var newReference = Uri.EscapeDataString(Path.GetFileName(finalFolder)) + "/";
        File.WriteAllText(finalFile, html.Replace(oldReference, newReference));
    }

    private static string CompanionFolderOf(string htmlPath)
    {
        var directory = Path.GetDirectoryName(htmlPath)!;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(htmlPath) + "_files");
    }

    private static void ApplyUndo(UndoStep step)
    {
        switch (step.Kind)
        {
            case UndoKind.DeleteOutput:
                DeleteOutput(step.Path);
                break;
            case UndoKind.MoveBack:
                File.Move(step.Path, step.OriginalPath!);
                ShellNotify.Renamed(step.Path, step.OriginalPath!);
                break;
            case UndoKind.RestoreFromRecycleBin:
                if (!RecycleBin.Restore(step.Path))
                {
                    throw new IOException($"Не нашёл {Path.GetFileName(step.Path)} в Корзине");
                }
                ShellNotify.Added(step.Path);
                break;
        }
    }

    private static void DeleteOutput(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            ShellNotify.RemovedFolder(path);
            return;
        }
        if (File.Exists(path))
        {
            File.Delete(path);
            ShellNotify.RemovedFile(path);
        }
        var companionFolder = CompanionFolderOf(path);
        if (FormatCatalog.ExtensionOf(path) == "html" && Directory.Exists(companionFolder))
        {
            Directory.Delete(companionFolder, recursive: true);
            ShellNotify.RemovedFolder(companionFolder);
        }
    }
}
