namespace Orbit.Core.Conversion;

public sealed record ConversionWork(
    string SourcePath,
    string TargetExtension,
    string OutputPath,
    IProgress<double> Progress);

public interface IFileConverter
{
    Task ConvertAsync(ConversionWork work, CancellationToken cancellation);
}

public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value)
    {
        report(value);
    }
}
