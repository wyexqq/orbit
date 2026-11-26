using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Orbit.Core;
using Orbit.Core.Formats;
using Orbit.Interop;
using Orbit.Ui;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Orbit.Ring;

internal sealed class RingWindow : Window
{
    private const double HintGap = 32;
    private const double CloseMilliseconds = 170;

    private readonly Canvas body = new();
    private readonly ScaleTransform bodyScale = new(1, 1);
    private readonly Ellipse hitDisc = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly Canvas extrasLayer = new();
    private readonly Canvas mainLayer = new();
    private readonly SolidColorBrush centerBrush = new();
    private readonly SolidColorBrush centerTextBrush = new();
    private readonly SolidColorBrush centerSubBrush = new();
    private readonly SolidColorBrush accentBrush = new();
    private readonly Path progressPath = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock centerLabel;
    private readonly TextBlock centerSub;
    private readonly Border hint = new();
    private readonly TextBlock hintTitle = new() { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock hintText = new();
    private readonly DropShadowEffect shadow = new() { BlurRadius = 28, ShadowDepth = 12, Direction = 270 };
    private readonly List<SectorVisual> mainSectors = [];
    private readonly List<SectorVisual> extraSectors = [];
    private readonly IDropTargetHelper? dropHelper = CreateDropHelper();
    private RingModel? model;
    private Palette palette = Palette.Dark;
    private NativeRect workArea;
    private IntPtr handle;
    private bool isClosing;
    private string? hintShownFor;

    public RingWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        AllowDrop = true;
        Width = RingGeometry.WindowSize;
        Height = RingGeometry.WindowSize;
        Title = "Orbit";

        centerLabel = CenteredText.Create(string.Empty, new Point(0, 0), 15, FontWeights.Bold, centerTextBrush);
        centerSub = CenteredText.Create(string.Empty, new Point(0, 11), 10, FontWeights.Normal, centerSubBrush);
        progressPath.Stroke = accentBrush;

        body.RenderTransform = bodyScale;
        body.Effect = shadow;
        body.Children.Add(extrasLayer);
        body.Children.Add(mainLayer);
        body.Children.Add(progressPath);
        body.Children.Add(new Ellipse
        {
            Width = RingGeometry.CenterRadius * 2,
            Height = RingGeometry.CenterRadius * 2,
            Fill = centerBrush,
            Margin = new Thickness(-RingGeometry.CenterRadius, -RingGeometry.CenterRadius, 0, 0),
        });
        body.Children.Add(centerLabel);
        body.Children.Add(centerSub);

        var origin = new Canvas { RenderTransform = new TranslateTransform(RingGeometry.WindowHalf, RingGeometry.WindowHalf) };
        origin.Children.Add(hitDisc);
        origin.Children.Add(body);

        hint.CornerRadius = new CornerRadius(16);
        hint.Padding = new Thickness(12, 6, 12, 6);
        hint.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { hintTitle, hintText } };
        hint.Opacity = 0;
        hint.IsHitTestVisible = false;
        hint.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.25 };
        TextElement.SetFontSize(hint, 12.5);
        TextElement.SetFontFamily(hint, new FontFamily("Segoe UI Variable Text, Segoe UI"));

        var root = new Canvas { Width = RingGeometry.WindowSize, Height = RingGeometry.WindowSize };
        root.Children.Add(origin);
        root.Children.Add(hint);
        Content = root;

        SourceInitialized += (_, _) => PrepareNativeWindow();
        new WindowInteropHelper(this).EnsureHandle();
    }

    public event Action<IReadOnlyList<string>>? FilesDragged;

    public event Action? DragLeft;

    public event Action<bool>? FilesDropped;

    public Func<DragDropEffects>? DropEffect { get; set; }

    public bool IsShowingRing => IsVisible && !isClosing;

    public void Open(RingModel ring, NativeRect monitorWorkArea, bool revealVisuals)
    {
        model = ring;
        workArea = monitorWorkArea;
        isClosing = false;
        palette = Palette.Current();
        ApplyPalette(Palette.Hex(ring.Accent));
        BuildSectors(ring);
        progressPath.Data = null;
        body.Opacity = revealVisuals ? 1 : 0;
        bodyScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        bodyScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        body.BeginAnimation(OpacityProperty, null);
        UpdateHitDisc();
        PlaceWindow();
        if (!IsVisible)
        {
            Show();
        }
        PlaceWindow();
        if (revealVisuals)
        {
            PlayEntrance();
        }
    }

    public void Reveal(RingModel ring)
    {
        model = ring;
        ApplyPalette(Palette.Hex(ring.Accent));
        BuildSectors(ring);
        UpdateHitDisc();
        PlaceWindow();
        PlayEntrance();
    }

    public void Paint()
    {
        if (model is null)
        {
            return;
        }
        var hovered = model.Hovered;
        for (var index = 0; index < mainSectors.Count; index++)
        {
            var isHovered = hovered is { Kind: HitKind.Main } && hovered.Value.Index == index;
            var isOpenMore = model.Main[index] == RingModel.More && model.ExtrasOpen;
            mainSectors[index].SetState(isHovered, isOpenMore);
        }
        for (var index = 0; index < extraSectors.Count; index++)
        {
            var isHovered = hovered is { Kind: HitKind.Extra } && hovered.Value.Index == index;
            extraSectors[index].SetState(isHovered, false);
        }
        PaintDigits();
        PaintCenter(model.TargetOf(hovered));
        PaintHint(model.TargetOf(hovered));
    }

    public void ShowExtras()
    {
        if (model is null)
        {
            return;
        }
        extrasLayer.Children.Clear();
        extraSectors.Clear();
        for (var index = 0; index < model.Extras.Count; index++)
        {
            var sector = new SectorVisual(
                model.Extras[index],
                index,
                model.ExtraLayout[index],
                RingGeometry.ExtraInnerRadius,
                RingGeometry.ExtraOuterRadius,
                palette,
                Palette.Hex(model.Accent),
                Palette.Hex(model.OnAccent));
            extraSectors.Add(sector);
            extrasLayer.Children.Add(sector.Root);
            sector.PlayEntrance();
        }
        UpdateHitDisc();
    }

    public void HideExtras()
    {
        extrasLayer.Children.Clear();
        extraSectors.Clear();
        UpdateHitDisc();
    }

    public void UpdateHitDisc()
    {
        if (model is null)
        {
            return;
        }
        var reach = model.ExtrasOpen ? RingGeometry.ExtraOuterRadius : RingGeometry.OuterRadius;
        var radius = reach + (model.ButtonHeld ? RingGeometry.Slack : 6);
        radius = Math.Min(radius, RingGeometry.WindowHalf - 1);
        hitDisc.Width = radius * 2;
        hitDisc.Height = radius * 2;
        hitDisc.Margin = new Thickness(-radius, -radius, 0, 0);
    }

    public void ShowWorking(double fraction)
    {
        foreach (var sector in mainSectors.Concat(extraSectors))
        {
            sector.FadeForWork();
        }
        HideHint();
        progressPath.Data = RingGeometry.ProgressArc(fraction);
    }

    public void ShowDone(string target)
    {
        progressPath.Data = RingGeometry.ProgressArc(1);
        var accent = Palette.Hex(model?.Accent ?? "#2F6FE0");
        var onAccent = Palette.Hex(model?.OnAccent ?? "#FFFFFF");
        Animate.ColorTo(centerBrush, accent, 150);
        Animate.ColorTo(centerTextBrush, onAccent, 150);
        Animate.ColorTo(centerSubBrush, onAccent, 150);
        SetCenterText(target.ToUpperInvariant(), "готово");
    }

    public void ShowCenterMessage(string label, string sub)
    {
        SetCenterText(label, sub);
    }

    public void CloseRing()
    {
        if (!IsVisible || isClosing)
        {
            return;
        }
        isClosing = true;
        HideHint();
        Animate.Double(bodyScale, ScaleTransform.ScaleXProperty, bodyScale.ScaleX, 0.82, CloseMilliseconds, Animate.Accelerate);
        Animate.Double(bodyScale, ScaleTransform.ScaleYProperty, bodyScale.ScaleY, 0.82, CloseMilliseconds, Animate.Accelerate);
        Animate.ElementDouble(body, OpacityProperty, body.Opacity, 0, CloseMilliseconds, Animate.Accelerate);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CloseMilliseconds + 10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (isClosing)
            {
                Hide();
                isClosing = false;
                model = null;
            }
        };
        timer.Start();
    }

    public void HideNow()
    {
        isClosing = false;
        model = null;
        Hide();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        var point = NativeMethods.CursorPosition();
        if (dropHelper is not null && e.Data is ComDataObject comData)
        {
            dropHelper.DragEnter(handle, comData, ref point, (int)e.Effects);
        }
        FilesDragged?.Invoke(FilesFrom(e.Data));
        e.Effects = DropEffect?.Invoke() ?? DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = DropEffect?.Invoke() ?? DragDropEffects.None;
        var point = NativeMethods.CursorPosition();
        dropHelper?.DragOver(ref point, (int)e.Effects);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        dropHelper?.DragLeave();
        DragLeft?.Invoke();
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        var effect = DropEffect?.Invoke() ?? DragDropEffects.None;
        var point = NativeMethods.CursorPosition();
        if (dropHelper is not null && e.Data is ComDataObject comData)
        {
            dropHelper.Drop(comData, ref point, (int)effect);
        }
        e.Effects = effect;
        e.Handled = true;
        if (effect != DragDropEffects.None)
        {
            var keepOriginal = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            Dispatcher.BeginInvoke(() => FilesDropped?.Invoke(keepOriginal));
        }
    }

    private static IReadOnlyList<string> FilesFrom(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                return paths.Where(FormatCatalog.IsConvertibleFile).ToList();
            }
        }
        catch (COMException error)
        {
            Log.Error("Не прочитал перетаскиваемые файлы", error);
        }
        return [];
    }

    private static IDropTargetHelper? CreateDropHelper()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ShellGuids.DragDropHelperClass);
            return type is null ? null : Activator.CreateInstance(type) as IDropTargetHelper;
        }
        catch (COMException error)
        {
            Log.Error("Нет помощника картинки перетаскивания", error);
            return null;
        }
    }

    private void PrepareNativeWindow()
    {
        handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeNonActivatingToolWindow(handle);
        HwndSource.FromHwnd(handle)?.AddHook(RefuseActivation);
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

    private void PlaceWindow()
    {
        if (model is null || handle == IntPtr.Zero)
        {
            return;
        }
        var size = (int)Math.Round(RingGeometry.WindowSize * model.Scale);
        var left = model.Center.X - size / 2;
        var top = model.Center.Y - size / 2;
        var flags = NativeMethods.NoActivateFlag;
        NativeMethods.SetWindowPos(handle, NativeMethods.TopmostWindow, left, top, 0, 0, flags | NativeMethods.NoSizeFlag);
        NativeMethods.SetWindowPos(handle, NativeMethods.TopmostWindow, left, top, size, size, flags);
    }

    private void ApplyPalette(Color accent)
    {
        centerBrush.Color = palette.Center;
        centerTextBrush.Color = palette.CenterText;
        centerSubBrush.Color = palette.DimText;
        accentBrush.Color = accent;
        shadow.Color = palette.Shadow;
        shadow.Opacity = palette.ShadowOpacity;
        hint.Background = new SolidColorBrush(palette.PillBackground);
        hintTitle.Foreground = new SolidColorBrush(palette.PillText);
        hintText.Foreground = new SolidColorBrush(palette.PillText);
    }

    private void BuildSectors(RingModel ring)
    {
        mainLayer.Children.Clear();
        extrasLayer.Children.Clear();
        mainSectors.Clear();
        extraSectors.Clear();
        var accent = Palette.Hex(ring.Accent);
        var onAccent = Palette.Hex(ring.OnAccent);
        for (var index = 0; index < ring.Main.Count; index++)
        {
            var sector = new SectorVisual(
                ring.Main[index],
                index,
                ring.MainLayout[index],
                RingGeometry.InnerRadius,
                RingGeometry.OuterRadius,
                palette,
                accent,
                onAccent);
            mainSectors.Add(sector);
            mainLayer.Children.Add(sector.Root);
        }
        HideHint();
    }

    private void PlayEntrance()
    {
        body.Opacity = 1;
        Animate.Double(bodyScale, ScaleTransform.ScaleXProperty, 0.55, 1, 200, Animate.Springy);
        Animate.Double(bodyScale, ScaleTransform.ScaleYProperty, 0.55, 1, 200, Animate.Springy);
        Animate.ElementDouble(body, OpacityProperty, 0, 1, 160, Animate.Snappy);
        foreach (var sector in mainSectors)
        {
            sector.PlayEntrance();
        }
        Paint();
    }

    private void PaintDigits()
    {
        var showsDigits = model?.Opener == RingOpener.Hotkey;
        var digitsOnExtras = model?.DigitsOnExtras == true;
        foreach (var sector in mainSectors)
        {
            sector.ShowDigit(showsDigits && !digitsOnExtras);
        }
        foreach (var sector in extraSectors)
        {
            sector.ShowDigit(showsDigits && digitsOnExtras);
        }
    }

    private void PaintCenter(string? target)
    {
        if (model is null)
        {
            return;
        }
        var source = SourceDescription.Of(model.Files);
        if (!model.HasTargets)
        {
            SetCenterText(source.Label, "нечего менять");
            return;
        }
        if (target is not null && target != RingModel.More)
        {
            SetCenterText(target.ToUpperInvariant(), $"из {source.Short}");
            return;
        }
        SetCenterText(source.Label, source.Sub);
    }

    private void SetCenterText(string label, string sub)
    {
        centerLabel.Text = label;
        centerSub.Text = sub;
        CenteredText.MoveTo(centerLabel, new Point(0, sub.Length > 0 ? -5 : 0));
        CenteredText.MoveTo(centerSub, new Point(0, 11));
    }

    private void PaintHint(string? target)
    {
        if (model is null || target is null || target == RingModel.More)
        {
            HideHint();
            return;
        }
        var hintKey = $"{target}:{model.ExtrasOpen}";
        if (hintKey == hintShownFor)
        {
            return;
        }
        hintShownFor = hintKey;
        var hintLine = FormatCatalog.HintFor(FormatCatalog.ExtensionOf(model.Files[0]), target);
        hintTitle.Text = target.ToUpperInvariant();
        hintText.Text = hintLine;
        hintText.Visibility = hintLine.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PositionHint();
        Animate.ElementDoubleTo(hint, OpacityProperty, 1, 120);
    }

    private void PositionHint()
    {
        if (model is null)
        {
            return;
        }
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var extents = model.CurrentExtents();
        var bottomOnScreen = model.Center.Y + (extents.Bottom + HintGap) * model.Scale;
        var top = bottomOnScreen <= workArea.Bottom
            ? RingGeometry.WindowHalf + extents.Bottom
            : RingGeometry.WindowHalf - extents.Top - HintGap;
        var left = RingGeometry.WindowHalf - hint.DesiredSize.Width / 2;
        Canvas.SetLeft(hint, Math.Max(0, left));
        Canvas.SetTop(hint, Math.Clamp(top, 0, RingGeometry.WindowSize - hint.DesiredSize.Height));
    }

    private void HideHint()
    {
        if (hintShownFor is null)
        {
            return;
        }
        hintShownFor = null;
        Animate.ElementDoubleTo(hint, OpacityProperty, 0, 100);
    }
}
