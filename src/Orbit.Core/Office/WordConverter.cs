using System.Runtime.InteropServices;
using Microsoft.Win32;
using Orbit.Core.Conversion;
using Orbit.Core.Formats;
using Orbit.Core.Text;

namespace Orbit.Core.Office;

public sealed class WordConverter : IFileConverter
{
    private const int DoNotSaveChanges = 0;
    private const int OpenAsEncodedText = 5;
    private const string WordOptionsKey = @"Software\Microsoft\Office\16.0\Word\Options";
    private const string PdfWarningValue = "DisableConvertPdfWarning";
    public const string NoPassword = "orbit-no-password";

    static WordConverter()
    {
        TurnOffPdfConversionWarning();
    }

    public Task ConvertAsync(ConversionWork work, CancellationToken cancellation)
    {
        return OfficeHost.Word.RunAsync(word => Convert(word, work), cancellation);
    }

    private static void TurnOffPdfConversionWarning()
    {
        try
        {
            using var options = Registry.CurrentUser.CreateSubKey(WordOptionsKey);
            if (options.GetValue(PdfWarningValue) is not 1)
            {
                options.SetValue(PdfWarningValue, 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error("Не отключил предупреждение Word про PDF", error);
        }
    }

    private static void Convert(dynamic word, ConversionWork work)
    {
        var document = Open(word, work.SourcePath);
        try
        {
            work.Progress.Report(0.4);
            Save(document, work.TargetExtension, work.OutputPath);
        }
        finally
        {
            document.Close(SaveChanges: DoNotSaveChanges);
            object releasedDocument = document;
            Marshal.FinalReleaseComObject(releasedDocument);
        }
        work.Progress.Report(1);
    }

    private static dynamic Open(dynamic word, string path)
    {
        var sourceExtension = FormatCatalog.ExtensionOf(path);
        if (sourceExtension is "txt" or "md")
        {
            return word.Documents.Open(
                FileName: path,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                PasswordDocument: NoPassword,
                Format: OpenAsEncodedText,
                Encoding: TextEncoding.DetectCodePage(path),
                Visible: false);
        }
        return word.Documents.Open(
            FileName: path,
            ConfirmConversions: false,
            ReadOnly: true,
            AddToRecentFiles: false,
            PasswordDocument: NoPassword,
            Visible: false);
    }

    private static void Save(dynamic document, string target, string outputPath)
    {
        var format = FormatFor(target);
        if (target is "txt" or "html")
        {
            document.SaveAs2(
                FileName: outputPath,
                FileFormat: format,
                AddToRecentFiles: false,
                Encoding: TextEncoding.Utf8CodePage);
            return;
        }
        document.SaveAs2(FileName: outputPath, FileFormat: format, AddToRecentFiles: false);
    }

    private static int FormatFor(string target)
    {
        return target switch
        {
            "docx" => 16,
            "doc" => 0,
            "rtf" => 6,
            "txt" => 7,
            "html" => 10,
            "pdf" => 17,
            "odt" => 23,
            _ => throw new NotSupportedException($"Word не сохраняет в {target}"),
        };
    }
}
