using Orbit.Core.Files;

namespace Orbit.Core.Conversion;

public sealed record ChainStep(IFileConverter Converter, string ProducesExtension);

public sealed class ChainedConverter(IReadOnlyList<ChainStep> intermediateSteps, IFileConverter finalConverter) : IFileConverter
{
    public async Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        var stepCount = intermediateSteps.Count + 1;
        var intermediateFiles = new List<string>();
        var currentSource = work.SourcePath;
        try
        {
            for (var index = 0; index < intermediateSteps.Count; index++)
            {
                var step = intermediateSteps[index];
                var intermediatePath = TemporaryFiles.NewSiblingPath(work.SourcePath, step.ProducesExtension);
                intermediateFiles.Add(intermediatePath);
                var stepWork = new ConversionWork(
                    currentSource,
                    step.ProducesExtension,
                    intermediatePath,
                    ScaledProgress(work.Progress, index, stepCount));
                await step.Converter.ConvertAsync(stepWork, cancellation);
                currentSource = intermediatePath;
            }
            var finalWork = new ConversionWork(
                currentSource,
                work.TargetExtension,
                work.OutputPath,
                ScaledProgress(work.Progress, stepCount - 1, stepCount));
            await finalConverter.ConvertAsync(finalWork, cancellation);
        }
        finally
        {
            foreach (var intermediatePath in intermediateFiles)
            {
                TemporaryFiles.DeleteWorkFolder(intermediatePath);
            }
        }
    }

    private static IProgress<double> ScaledProgress(IProgress<double> overall, int stepIndex, int stepCount)
    {
        return new SyncProgress<double>(fraction => overall.Report((stepIndex + fraction) / stepCount));
    }
}
