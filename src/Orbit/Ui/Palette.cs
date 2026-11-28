using System.Windows.Media;
using Microsoft.Win32;

namespace Orbit.Ui;

internal sealed record Palette(
    Color Sector,
    Color SectorText,
    Color DimText,
    Color Shadow,
    double ShadowOpacity,
    Color PillBackground,
    Color PillText,
    Color PillDim)
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static Palette Dark { get; } = new(
        Hex("#23262E"),
        Hex("#ECEEF3"),
        Hex("#8C93A1"),
        Colors.Black,
        0.5,
        Color.FromArgb(242, 24, 27, 33),
        Hex("#ECEEF3"),
        Hex("#9AA1AE"));

    public static Palette Light { get; } = new(
        Hex("#FFFFFF"),
        Hex("#1A1D23"),
        Hex("#7A8190"),
        Color.FromRgb(8, 16, 36),
        0.32,
        Color.FromArgb(247, 255, 255, 255),
        Hex("#15171C"),
        Hex("#646B78"));

    public static Palette Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        var appsUseLight = key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        return appsUseLight ? Light : Dark;
    }

    public static Color Hex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    public static Color Mix(Color first, Color second, double firstShare)
    {
        byte Blend(byte a, byte b) => (byte)Math.Round(a * firstShare + b * (1 - firstShare));
        return Color.FromArgb(Blend(first.A, second.A), Blend(first.R, second.R), Blend(first.G, second.G), Blend(first.B, second.B));
    }

    public static Color WithAlpha(Color color, double alpha)
    {
        return Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);
    }
}
