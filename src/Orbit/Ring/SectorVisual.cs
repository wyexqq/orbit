using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Orbit.Ui;

namespace Orbit.Ring;

internal sealed class SectorVisual
{
    private const double HoverMilliseconds = 120;
    private const double OpenShare = 0.28;

    private readonly SolidColorBrush shapeBrush;
    private readonly SolidColorBrush labelBrush;
    private readonly SolidColorBrush digitBrush;
    private readonly TranslateTransform hoverShift = new();
    private readonly ScaleTransform entranceScale = new(1, 1);
    private readonly RotateTransform entranceRotation = new();
    private readonly Vector shiftDirection;
    private readonly Palette palette;
    private readonly Color accent;
    private readonly Color onAccent;
    private readonly bool isMore;
    private bool isHovered;
    private bool isOpen;
    private bool digitVisible;

    public SectorVisual(
        string target,
        int index,
        SectorArc arc,
        double innerRadius,
        double outerRadius,
        Palette palette,
        Color accent,
        Color onAccent)
    {
        this.palette = palette;
        this.accent = accent;
        this.onAccent = onAccent;
        isMore = target == RingModel.More;
        shapeBrush = new SolidColorBrush(palette.Sector);
        labelBrush = new SolidColorBrush(isMore ? palette.DimText : palette.SectorText);
        digitBrush = new SolidColorBrush(palette.DimText);
        var shift = RingGeometry.Polar(RingGeometry.HoverShift, arc.Middle);
        shiftDirection = new Vector(shift.X, shift.Y);
        Root = new Canvas
        {
            RenderTransform = new TransformGroup { Children = { entranceScale, entranceRotation, hoverShift } },
        };
        Root.Children.Add(new Path
        {
            Data = RingGeometry.AnnularSector(innerRadius, outerRadius, arc),
            Fill = shapeBrush,
            Stroke = shapeBrush,
            StrokeThickness = RingGeometry.Corner,
            StrokeLineJoin = PenLineJoin.Round,
        });
        var labelPoint = RingGeometry.Polar((innerRadius + outerRadius) / 2, arc.Middle);
        var label = CenteredText.Create(
            isMore ? "ещё" : target.ToUpperInvariant(),
            labelPoint,
            12.5,
            isMore ? FontWeights.Medium : FontWeights.SemiBold,
            labelBrush);
        Root.Children.Add(label);
        var digitPoint = RingGeometry.Polar(outerRadius - 12, arc.Middle);
        Digit = CenteredText.Create((index + 1).ToString(), digitPoint, 9.5, FontWeights.SemiBold, digitBrush);
        Digit.Opacity = 0;
        Root.Children.Add(Digit);
        Order = index;
    }

    public Canvas Root { get; }

    public FrameworkElement Digit { get; }

    public int Order { get; }

    public void PlayEntrance()
    {
        var delay = Order * 18;
        Animate.Double(entranceScale, ScaleTransform.ScaleXProperty, 0.7, 1, 240, Animate.Springy, delay);
        Animate.Double(entranceScale, ScaleTransform.ScaleYProperty, 0.7, 1, 240, Animate.Springy, delay);
        Animate.Double(entranceRotation, RotateTransform.AngleProperty, -14, 0, 240, Animate.Springy, delay);
        Animate.ElementDouble(Root, UIElement.OpacityProperty, 0, 1, 200, Animate.Snappy, delay);
    }

    public void SetState(bool hovered, bool open)
    {
        if (hovered == isHovered && open == isOpen)
        {
            return;
        }
        isHovered = hovered;
        isOpen = open;
        var shift = hovered ? shiftDirection : new Vector();
        Animate.DoubleTo(hoverShift, TranslateTransform.XProperty, shift.X, 140, Animate.Snappy);
        Animate.DoubleTo(hoverShift, TranslateTransform.YProperty, shift.Y, 140, Animate.Snappy);
        Animate.ColorTo(shapeBrush, ShapeColor(), HoverMilliseconds);
        Animate.ColorTo(labelBrush, LabelColor(), HoverMilliseconds);
        Animate.ColorTo(digitBrush, hovered ? onAccent : palette.DimText, HoverMilliseconds);
    }

    public void ShowDigit(bool visible)
    {
        if (visible == digitVisible)
        {
            return;
        }
        digitVisible = visible;
        Animate.ElementDoubleTo(Digit, UIElement.OpacityProperty, visible ? 0.9 : 0, HoverMilliseconds);
    }

    public void FadeForWork()
    {
        Digit.Visibility = Visibility.Hidden;
        if (!isHovered)
        {
            Animate.ElementDoubleTo(Root, UIElement.OpacityProperty, 0, 150);
        }
    }

    private Color ShapeColor()
    {
        if (isHovered)
        {
            return accent;
        }
        return isOpen ? Palette.Mix(accent, palette.Sector, OpenShare) : palette.Sector;
    }

    private Color LabelColor()
    {
        if (isHovered)
        {
            return onAccent;
        }
        return isMore ? palette.DimText : palette.SectorText;
    }
}

internal static class CenteredText
{
    private const double BoxWidth = 120;
    private const double BoxHeight = 30;

    public static TextBlock Create(string text, Point center, double fontSize, FontWeight weight, Brush foreground)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = BoxWidth,
            Height = BoxHeight,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground,
            TextAlignment = TextAlignment.Center,
            IsHitTestVisible = false,
        };
        block.Padding = new Thickness(0, (BoxHeight - fontSize * 1.33) / 2, 0, 0);
        Canvas.SetLeft(block, center.X - BoxWidth / 2);
        Canvas.SetTop(block, center.Y - BoxHeight / 2);
        return block;
    }

    public static void MoveTo(FrameworkElement block, Point center)
    {
        Canvas.SetLeft(block, center.X - BoxWidth / 2);
        Canvas.SetTop(block, center.Y - BoxHeight / 2);
    }
}
