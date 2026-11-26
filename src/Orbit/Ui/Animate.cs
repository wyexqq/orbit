using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Orbit.Ui;

internal static class Animate
{
    public static readonly IEasingFunction Snappy = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction Springy = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
    public static readonly IEasingFunction Accelerate = new QuadraticEase { EasingMode = EasingMode.EaseIn };

    public static void Double(
        Animatable target,
        DependencyProperty property,
        double from,
        double to,
        double milliseconds,
        IEasingFunction? easing = null,
        double delayMilliseconds = 0)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = easing,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            FillBehavior = FillBehavior.HoldEnd,
        };
        target.BeginAnimation(property, animation);
    }

    public static void DoubleTo(Animatable target, DependencyProperty property, double to, double milliseconds, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing };
        target.BeginAnimation(property, animation);
    }

    public static void ElementDouble(
        UIElement target,
        DependencyProperty property,
        double from,
        double to,
        double milliseconds,
        IEasingFunction? easing = null,
        double delayMilliseconds = 0)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = easing,
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
        };
        target.BeginAnimation(property, animation);
    }

    public static void ElementDoubleTo(UIElement target, DependencyProperty property, double to, double milliseconds)
    {
        target.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)));
    }

    public static void ColorTo(SolidColorBrush brush, Color to, double milliseconds)
    {
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(milliseconds)));
    }

    public static void Stop(Animatable target, DependencyProperty property)
    {
        target.BeginAnimation(property, null);
    }
}
