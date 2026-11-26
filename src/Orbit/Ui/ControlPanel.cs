using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Orbit.Interop;
using Orbit.Settings;

namespace Orbit.Ui;

internal sealed class ControlPanel : Window
{
    private const double PanelWidth = 300;
    private const double ShadowRoomSide = 20;
    private const double ShadowRoomTop = 12;
    private const double ShadowRoomBottom = 28;
    private const double SwitchTravel = 18;
    private const string RunningText = "Работает — зажмите файл или нажмите Alt+Q";
    private const string JustStartedText = "Включён — зажмите файл или нажмите Alt+Q";

    private readonly TextBlock title = new() { Text = "Orbit", FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock subtitle = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Border closeButton = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -4, -6, 0), ToolTip = "Закрыть" };
    private readonly TextBlock closeGlyph = new() { Text = "✕", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border autostartRow = new() { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
    private readonly TextBlock autostartText = new() { Text = "Запускать вместе с Windows", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border switchTrack = new() { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse switchThumb = new() { Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
    private readonly TranslateTransform thumbShift = new();
    private readonly Border turnOffButton = new() { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand };
    private readonly TextBlock turnOffText = new() { Text = "Выключить Orbit", FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border card = new() { Width = PanelWidth, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 16) };
    private readonly TranslateTransform slide = new();
    private Palette palette = Palette.Dark;
    private IntPtr handle;

    public ControlPanel()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Orbit";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        closeButton.Child = closeGlyph;
        closeButton.MouseEnter += (_, _) => PaintCloseButton(hovered: true);
        closeButton.MouseLeave += (_, _) => PaintCloseButton(hovered: false);
        closeButton.MouseLeftButtonUp += (_, _) => HidePanel();

        switchThumb.RenderTransform = thumbShift;
        switchThumb.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.3 };
        switchTrack.Child = switchThumb;
        autostartRow.Child = BuildAutostartLayout();
        autostartRow.MouseLeftButtonUp += (_, _) => FlipAutostart();

        turnOffButton.Child = turnOffText;
        turnOffButton.MouseEnter += (_, _) => PaintTurnOffButton(hovered: true);
        turnOffButton.MouseLeave += (_, _) => PaintTurnOffButton(hovered: false);
        turnOffButton.MouseLeftButtonUp += (_, _) => TurnOffRequested?.Invoke();

        card.Child = new StackPanel { Children = { BuildHeader(), autostartRow, turnOffButton } };
        card.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 12, Direction = 270, Opacity = 0.35 };
        card.Margin = new Thickness(ShadowRoomSide, ShadowRoomTop, ShadowRoomSide, ShadowRoomBottom);
        card.RenderTransform = slide;
        Content = card;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
            }
        };
        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => PlaceInMiddle();
        SourceInitialized += (_, _) => handle = new WindowInteropHelper(this).Handle;
        new WindowInteropHelper(this).EnsureHandle();
    }

    public event Action? TurnOffRequested;

    public void ShowPanel(bool justStarted)
    {
        palette = Palette.Current();
        ApplyPalette();
        subtitle.Text = justStarted ? JustStartedText : RunningText;
        PaintSwitch(AppSettings.StartsWithWindows, animate: false);
        var wasVisible = IsVisible;
        if (!wasVisible)
        {
            card.BeginAnimation(OpacityProperty, null);
            card.Opacity = 0;
            Show();
        }
        PlaceInMiddle();
        Activate();
        if (!wasVisible)
        {
            Animate.ElementDouble(card, OpacityProperty, 0, 1, 180, Animate.Snappy);
            Animate.Double(slide, TranslateTransform.YProperty, 8, 0, 180, Animate.Snappy);
        }
    }

    private void HidePanel()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private Grid BuildHeader()
    {
        var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Children = { title, subtitle } };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = OrbitMark.Create(36);
        mark.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(mark);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        Grid.SetColumn(closeButton, 2);
        header.Children.Add(closeButton);
        return header;
    }

    private Grid BuildAutostartLayout()
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.Children.Add(autostartText);
        Grid.SetColumn(switchTrack, 1);
        switchTrack.Margin = new Thickness(12, 0, 0, 0);
        layout.Children.Add(switchTrack);
        return layout;
    }

    private void FlipAutostart()
    {
        var turnOn = !AppSettings.StartsWithWindows;
        AppSettings.StartsWithWindows = turnOn;
        PaintSwitch(AppSettings.StartsWithWindows, animate: true);
    }

    private void PaintSwitch(bool isOn, bool animate)
    {
        var trackColor = isOn ? OrbitMark.Accent : Palette.WithAlpha(palette.PillText, 0.22);
        switchTrack.Background = new SolidColorBrush(trackColor);
        var thumbX = isOn ? SwitchTravel : 0;
        if (animate)
        {
            Animate.DoubleTo(thumbShift, TranslateTransform.XProperty, thumbX, 140, Animate.Snappy);
            return;
        }
        Animate.Stop(thumbShift, TranslateTransform.XProperty);
        thumbShift.X = thumbX;
    }

    private void ApplyPalette()
    {
        card.Background = new SolidColorBrush(palette.PillBackground);
        title.Foreground = new SolidColorBrush(palette.PillText);
        subtitle.Foreground = new SolidColorBrush(palette.PillDim);
        closeGlyph.Foreground = new SolidColorBrush(palette.PillDim);
        autostartText.Foreground = new SolidColorBrush(palette.PillText);
        autostartRow.BorderBrush = new SolidColorBrush(Palette.WithAlpha(palette.PillText, 0.09));
        turnOffText.Foreground = new SolidColorBrush(palette.PillText);
        PaintCloseButton(hovered: false);
        PaintTurnOffButton(hovered: false);
    }

    private void PaintCloseButton(bool hovered)
    {
        closeButton.Background = new SolidColorBrush(Palette.WithAlpha(palette.PillText, hovered ? 0.14 : 0));
    }

    private void PaintTurnOffButton(bool hovered)
    {
        turnOffButton.Background = new SolidColorBrush(Palette.WithAlpha(palette.PillText, hovered ? 0.17 : 0.10));
    }

    private void PlaceInMiddle()
    {
        if (handle == IntPtr.Zero || !IsVisible)
        {
            return;
        }
        var (workArea, scale) = NativeMethods.MonitorAt(NativeMethods.CursorPosition());
        var width = (int)Math.Round(ActualWidth * scale);
        var height = (int)Math.Round(ActualHeight * scale);
        var left = workArea.Left + (workArea.Width - width) / 2;
        var top = workArea.Top + (workArea.Height - height) / 2;
        NativeMethods.SetWindowPos(handle, NativeMethods.TopmostWindow, left, top, 0, 0, NativeMethods.NoSizeFlag);
    }
}
