using System.Runtime.InteropServices;
using Orbit.Core.Conversion;
using Orbit.Core.Files;
using Orbit.Core.Formats;
using Orbit.Core.Text;

namespace Orbit.Core.Office;

public sealed class ExcelConverter : IFileConverter
{
    private const int DontUpdateLinks = 0;
    private const int PdfExport = 0;
    private const int DelimitedText = 1;
    private const int DoubleQuoteQualifier = 1;
    private const int Utf8Csv = 62;

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return OfficeHost.Excel.RunAsync(excel => Convert(excel, work), cancellation);
    }

    private static void Convert(dynamic excel, ConversionWork work)
    {
        var textCopy = FormatCatalog.ExtensionOf(work.SourcePath) == "csv"
            ? TemporaryFiles.NewPath("txt")
            : null;
        var workbook = textCopy is null
            ? excel.Workbooks.Open(Filename: work.SourcePath, UpdateLinks: DontUpdateLinks, ReadOnly: true, Password: WordConverter.NoPassword)
            : OpenCsv(excel, work.SourcePath, textCopy);
        try
        {
            work.Progress.Report(0.4);
            Save(workbook, work.TargetExtension, work.OutputPath);
        }
        finally
        {
            workbook.Close(false);
            object releasedWorkbook = workbook;
            Marshal.FinalReleaseComObject(releasedWorkbook);
            if (textCopy is not null)
            {
                TemporaryFiles.Delete(textCopy);
            }
        }
        work.Progress.Report(1);
    }

    private static dynamic OpenCsv(dynamic excel, string csvPath, string textCopy)
    {
        File.Copy(csvPath, textCopy, overwrite: true);
        var firstLine = TextEncoding.ReadAllText(csvPath).Split('\n').FirstOrDefault() ?? string.Empty;
        var delimiter = GuessDelimiter(firstLine);
        excel.Workbooks.OpenText(
            Filename: textCopy,
            Origin: TextEncoding.DetectCodePage(csvPath),
            StartRow: 1,
            DataType: DelimitedText,
            TextQualifier: DoubleQuoteQualifier,
            ConsecutiveDelimiter: false,
            Tab: delimiter == '\t',
            Semicolon: delimiter == ';',
            Comma: delimiter == ',',
            Local: true);
        var workbookCount = (int)excel.Workbooks.Count;
        return excel.Workbooks[workbookCount];
    }

    private static char GuessDelimiter(string firstLine)
    {
        char[] candidates = [';', ',', '\t'];
        return candidates.MaxBy(candidate => firstLine.Count(symbol => symbol == candidate));
    }

    private static void Save(dynamic workbook, string target, string outputPath)
    {
        switch (target)
        {
            case "pdf":
                workbook.ExportAsFixedFormat(PdfExport, outputPath);
                break;
            case "csv":
                workbook.SaveAs(Filename: outputPath, FileFormat: Utf8Csv, Local: true);
                break;
            default:
                workbook.SaveAs(outputPath, FormatFor(target));
                break;
        }
    }

    private static int FormatFor(string target)
    {
        return target switch
        {
            "xlsx" => 51,
            "xls" => 56,
            "ods" => 60,
            "txt" => 42,
            _ => throw new NotSupportedException($"Excel не сохраняет в {target}"),
        };
    }
}
