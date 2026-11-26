using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Orbit.Interop;
using Orbit.Settings;

namespace Orbit.Ui;

internal sealed record ToastContent(
    string Title,
    string Sub,
    string ActionLabel = "",
    Action? Action = null,
    double? Progress = null,
    string Accent = "#2F6FE0",
    int AutoHideMilliseconds = 0);

internal sealed class ToastDrag(NativePoint cursorAtStart, NativeRect windowAtStart)
{
    public NativePoint CursorAtStart { get; } = cursorAtStart;

    public NativeRect WindowAtStart { get; } = windowAtStart;

    public bool ResumeAutoHide { get; set; }

    public bool Moved { get; set; }
}

internal sealed class ToastWindow : Window
{
    private const double ToastWidth = 330;
    private const double CardSidePadding = 26;
    private const double ScreenMargin = 12;
    private const double ShadowRoomSide = 20;
    private const double ShadowRoomTop = 12;
    private const double ShadowRoomBottom = 28;
    private const int DragThreshold = 3;

    private readonly AppSettings settings;
    private readonly TextBlock title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock sub = new() { FontSize = 12, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Border actionButton = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock actionText = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
    private readonly Border closeButton = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, -4, -5, 0), ToolTip = "Скрыть" };
    private readonly TextBlock closeGlyph = new() { Text = "✕", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border progressTrack = new() { Height = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 9, 0, 0) };
    private readonly Border progressFill = new() { Height = 3, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Border card = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 11, 12, 11) };
    private readonly TranslateTransform slide = new();
    private readonly DispatcherTimer hideTimer = new();
    private Action? action;
    private Palette palette = Palette.Dark;
    private IntPtr handle;
    private ToastDrag? drag;

    public ToastWindow(AppSettings settings)
    {
        this.settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Orbit";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        actionButton.Child = actionText;
        actionButton.MouseEnter += (_, _) => PaintActionButton(hovered: true);
        actionButton.MouseLeave += (_, _) => PaintActionButton(hovered: false);
        actionButton.MouseLeftButtonUp += (_, _) => action?.Invoke();
        closeButton.Child = closeGlyph;
        closeButton.MouseEnter += (_, _) => PaintCloseButton(hovered: true);
        closeButton.MouseLeave += (_, _) => PaintCloseButton(hovered: false);
        closeButton.MouseLeftButtonUp += (_, _) => HideToast();
        progressTrack.Child = progressFill;

        var texts = new StackPanel { Children = { title, sub } };
        var layout = new Grid { Width = ToastWidth - CardSidePadding };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(texts);
        Grid.SetColumn(actionButton, 1);
        actionButton.Margin = new Thickness(12, 0, 0, 0);
        layout.Children.Add(actionButton);
        Grid.SetColumn(closeButton, 2);
        layout.Children.Add(closeButton);
        Grid.SetRow(progressTrack, 1);
        Grid.SetColumnSpan(progressTrack, 3);
        layout.Children.Add(progressTrack);

        card.Child = layout;
        card.Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 12, Direction = 270, Opacity = 0.35 };
        card.Margin = new Thickness(ShadowRoomSide, ShadowRoomTop, ShadowRoomSide, ShadowRoomBottom);
        card.RenderTransform = slide;
        card.MouseLeftButtonDown += StartDrag;
        card.MouseMove += (_, _) => ContinueDrag();
        card.MouseLeftButtonUp += (_, _) => FinishDrag();
        card.LostMouseCapture += (_, _) => FinishDrag();
        Content = card;

        hideTimer.Tick += (_, _) => HideToast();
        SizeChanged += (_, _) => PlaceWindow();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeNonActivatingToolWindow(handle);
            HwndSource.FromHwnd(handle)?.AddHook(RefuseActivation);
        };
        new WindowInteropHelper(this).EnsureHandle();
    }

    public void ShowContent(ToastContent content)
    {
        hideTimer.Stop();
        palette = Palette.Current();
        ApplyPalette(Palette.Hex(content.Accent));
        title.Text = content.Title;
        sub.Text = content.Sub;
        sub.Visibility = content.Sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        action = content.Action;
        actionText.Text = content.ActionLabel;
        actionButton.Visibility = content.ActionLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        progressTrack.Visibility = content.Progress is null ? Visibility.Collapsed : Visibility.Visible;
        SetProgressWidth(content.Progress ?? 0);
        var wasVisible = IsVisible;
        if (!wasVisible)
        {
            Show();
        }
        PlaceWindow();
        if (!wasVisible)
        {
            Animate.ElementDouble(card, OpacityProperty, 0, 1, 180, Animate.Snappy);
            Animate.Double(slide, TranslateTransform.YProperty, 8, 0, 180, Animate.Snappy);
        }
        var autoHides = content.AutoHideMilliseconds > 0;
        if (autoHides)
        {
            hideTimer.Interval = TimeSpan.FromMilliseconds(content.AutoHideMilliseconds);
        }
        if (drag is not null)
        {
            drag.ResumeAutoHide = autoHides;
        }
        else if (autoHides)
        {
            hideTimer.Start();
        }
    }

    public void UpdateProgress(double fraction, string line)
    {
        SetProgressWidth(fraction);
        sub.Text = line;
    }

    public void HideToast()
    {
        hideTimer.Stop();
        action = null;
        drag = null;
        card.ReleaseMouseCapture();
        Hide();
    }

    private void StartDrag(object sender, MouseButtonEventArgs e)
    {
        if (actionButton.IsMouseOver || closeButton.IsMouseOver)
        {
            return;
        }
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            ReturnToCorner();
            return;
        }
        NativeMethods.GetWindowRect(handle, out var windowRect);
        drag = new ToastDrag(NativeMethods.CursorPosition(), windowRect) { ResumeAutoHide = hideTimer.IsEnabled };
        hideTimer.Stop();
        card.CaptureMouse();
    }

    private void ContinueDrag()
    {
        if (drag is null)
        {
            return;
        }
        var cursor = NativeMethods.CursorPosition();
        var shiftX = cursor.X - drag.CursorAtStart.X;
        var shiftY = cursor.Y - drag.CursorAtStart.Y;
        if (!drag.Moved && Math.Abs(shiftX) + Math.Abs(shiftY) < DragThreshold)
        {
            return;
        }
        drag.Moved = true;
        MoveTo(drag.WindowAtStart.Left + shiftX, drag.WindowAtStart.Top + shiftY);
    }

    private void FinishDrag()
    {
        var finished = drag;
        if (finished is null)
        {
            return;
        }
        drag = null;
        card.ReleaseMouseCapture();
        if (finished.ResumeAutoHide)
        {
            hideTimer.Start();
        }
        if (!finished.Moved)
        {
            return;
        }
        NativeMethods.GetWindowRect(handle, out var dropped);
        var (left, top) = KeepOnScreen(dropped.Left, dropped.Top);
        settings.ToastLeft = left;
        settings.ToastTop = top;
        settings.Save();
        MoveTo(left, top);
    }

    private void ReturnToCorner()
    {
        settings.ToastLeft = null;
        settings.ToastTop = null;
        settings.Save();
        PlaceWindow();
    }

    private void SetProgressWidth(double fraction)
    {
        var trackWidth = ToastWidth - CardSidePadding;
        progressFill.Width = trackWidth * Math.Clamp(fraction, 0, 1);
    }

    private void ApplyPalette(Color accent)
    {
        card.Background = new SolidColorBrush(palette.PillBackground);
        title.Foreground = new SolidColorBrush(palette.PillText);
        sub.Foreground = new SolidColorBrush(palette.PillDim);
        actionText.Foreground = new SolidColorBrush(palette.PillText);
        closeGlyph.Foreground = new SolidColorBrush(palette.PillDim);
        progressTrack.Background = new SolidColorBrush(Palette.WithAlpha(palette.PillText, 0.12));
        progressFill.Background = new SolidColorBrush(accent);
        PaintActionButton(hovered: false);
        PaintCloseButton(hovered: false);
    }

    private void PaintActionButton(bool hovered)
    {
        actionButton.Background = new SolidColorBrush(Palette.WithAlpha(palette.PillText, hovered ? 0.17 : 0.10));
    }

    private void PaintCloseButton(bool hovered)
    {
        closeButton.Background = new SolidColorBrush(Palette.WithAlpha(palette.PillText, hovered ? 0.14 : 0));
    }

    private void PlaceWindow()
    {
        if (handle == IntPtr.Zero || !IsVisible || drag is not null)
        {
            return;
        }
        var (left, top) = settings.ToastLeft is int savedLeft && settings.ToastTop is int savedTop
            ? KeepOnScreen(savedLeft, savedTop)
            : CornerPosition();
        MoveTo(left, top);
    }

    private (int Left, int Top) CornerPosition()
    {
        var (workArea, scale) = NativeMethods.MonitorAt(NativeMethods.CursorPosition());
        var (width, height) = PhysicalSize(scale);
        var shadowOverhang = (int)Math.Round((ShadowRoomSide - ScreenMargin) * scale);
        return (workArea.Right - width + shadowOverhang, workArea.Bottom - height);
    }

    private (int Left, int Top) KeepOnScreen(int left, int top)
    {
        var (_, cornerScale) = NativeMethods.MonitorAt(new NativePoint(left, top));
        var (roughWidth, roughHeight) = PhysicalSize(cornerScale);
        var middle = new NativePoint(left + roughWidth / 2, top + roughHeight / 2);
        var (workArea, scale) = NativeMethods.MonitorAt(middle);
        var (width, height) = PhysicalSize(scale);
        var side = (int)Math.Round(ShadowRoomSide * scale);
        var above = (int)Math.Round(ShadowRoomTop * scale);
        var below = (int)Math.Round(ShadowRoomBottom * scale);
        var keptLeft = Math.Clamp(left, workArea.Left - side, Math.Max(workArea.Left - side, workArea.Right - width + side));
        var keptTop = Math.Clamp(top, workArea.Top - above, Math.Max(workArea.Top - above, workArea.Bottom - height + below));
        return (keptLeft, keptTop);
    }

    private (int Width, int Height) PhysicalSize(double scale)
    {
        return ((int)Math.Round(ActualWidth * scale), (int)Math.Round(ActualHeight * scale));
    }

    private void MoveTo(int left, int top)
    {
        NativeMethods.SetWindowPos(handle, NativeMethods.TopmostWindow, left, top, 0, 0, NativeMethods.NoActivateFlag | NativeMethods.NoSizeFlag);
    }

    private static IntPtr RefuseActivation(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.MouseActivateMessage)
        {
            handled = true;
            return new IntPtr(NativeMethods.NoActivateReply);
        }
        return IntPtr.Zero;
    }
}
