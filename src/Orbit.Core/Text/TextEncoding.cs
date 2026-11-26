using System.Text;

namespace Orbit.Core.Text;

public static class TextEncoding
{
    public const int Utf8CodePage = 65001;
    public const int CyrillicCodePage = 1251;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Detect(byte[] bytes)
    {
        if (HasUtf8Mark(bytes))
        {
            return Encoding.UTF8;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }
        return IsValidUtf8(bytes) ? Encoding.UTF8 : Encoding.GetEncoding(CyrillicCodePage);
    }

    public static int DetectCodePage(string path)
    {
        return Detect(File.ReadAllBytes(path)).CodePage;
    }

    public static string ReadAllText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = Detect(bytes);
        var preamble = encoding.GetPreamble();
        var hasPreamble = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        var body = hasPreamble ? bytes.AsSpan(preamble.Length) : bytes.AsSpan();
        return encoding.GetString(body);
    }

    public static void WriteAllText(string path, string text)
    {
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool HasUtf8Mark(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
