using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Orbit.Core.Conversion;

public sealed class PdfPagesConverter : IFileConverter
{
    private const double RenderScale = 2;

    public async Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        var file = await StorageFile.GetFileFromPathAsync(work.SourcePath);
        var document = await PdfDocument.LoadFromFileAsync(file);
        if (document.IsPasswordProtected)
        {
            throw new InvalidOperationException("PDF защищён паролем");
        }
        Directory.CreateDirectory(work.OutputPath);
        var baseName = Path.GetFileNameWithoutExtension(work.SourcePath);
        var pageCount = (int)document.PageCount;
        var digits = Math.Max(2, pageCount.ToString().Length);
        for (var index = 0; index < pageCount; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var pngBytes = await RenderPageAsync(document, (uint)index);
            var number = (index + 1).ToString().PadLeft(digits, '0');
            var imagePath = Path.Combine(work.OutputPath, $"{baseName} {number}.{work.TargetExtension}");
            await SaveAsync(pngBytes, work.TargetExtension, imagePath, cancellation);
            work.Progress.Report((double)(index + 1) / pageCount);
        }
    }

    private static async Task<byte[]> RenderPageAsync(PdfDocument document, uint index)
    {
        using var page = document.GetPage(index);
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Round(page.Size.Width * RenderScale),
            DestinationHeight = (uint)Math.Round(page.Size.Height * RenderScale),
        };
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task SaveAsync(byte[] pngBytes, string target, string imagePath, CancellationToken cancellation)
    {
        if (target == "png")
        {
            await File.WriteAllBytesAsync(imagePath, pngBytes, cancellation);
            return;
        }
        await Task.Run(() => ImageConverter.ConvertEncodedImage(pngBytes, target, imagePath), cancellation);
    }
}
