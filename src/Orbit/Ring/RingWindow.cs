using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private const double CloseMilliseconds = 170;

    private readonly Canvas body = new();
    private readonly ScaleTransform bodyScale = new(1, 1);
    private readonly Ellipse hitDisc = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    private readonly Canvas extrasLayer = new();
    private readonly Canvas mainLayer = new();
    private readonly SolidColorBrush accentBrush = new();
    private readonly Path progressPath = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly DropShadowEffect shadow = new() { BlurRadius = 28, ShadowDepth = 12, Direction = 270 };
    private readonly List<SectorVisual> mainSectors = [];
    private readonly List<SectorVisual> extraSectors = [];
    private readonly IDropTargetHelper? dropHelper = CreateDropHelper();
    private RingModel? model;
    private Palette palette = Palette.Dark;
    private IntPtr handle;
    private bool isClosing;

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

        progressPath.Stroke = accentBrush;

        body.RenderTransform = bodyScale;
        body.Effect = shadow;
        body.Children.Add(extrasLayer);
        body.Children.Add(mainLayer);
        body.Children.Add(progressPath);

        var origin = new Canvas { RenderTransform = new TranslateTransform(RingGeometry.WindowHalf, RingGeometry.WindowHalf) };
        origin.Children.Add(hitDisc);
        origin.Children.Add(body);

        var root = new Canvas { Width = RingGeometry.WindowSize, Height = RingGeometry.WindowSize };
        root.Children.Add(origin);
        Content = root;

        SourceInitialized += (_, _) => PrepareNativeWindow();
        new WindowInteropHelper(this).EnsureHandle();
    }

    public event Action<IReadOnlyList<string>>? FilesDragged;

    public event Action? DragLeft;

    public event Action<bool>? FilesDropped;

    public Func<DragDropEffects>? DropEffect { get; set; }

    public bool IsShowingRing => IsVisible && !isClosing;

    public void Open(RingModel ring, bool revealVisuals)
    {
        model = ring;
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
                model.Size.ExtraInner,
                model.Size.ExtraOuter,
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
        var reach = model.ExtrasOpen ? model.Size.ExtraOuter : model.Size.Outer;
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
        progressPath.Data = RingGeometry.ProgressArc(fraction, ProgressRadius());
    }

    public void ShowDone()
    {
        progressPath.Data = RingGeometry.ProgressArc(1, ProgressRadius());
    }

    public void CloseRing()
    {
        if (!IsVisible || isClosing)
        {
            return;
        }
        isClosing = true;
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

    private double ProgressRadius()
    {
        return (model?.Size ?? RingSize.Standard).ProgressRadius;
    }

    private void ApplyPalette(Color accent)
    {
        accentBrush.Color = accent;
        shadow.Color = palette.Shadow;
        shadow.Opacity = palette.ShadowOpacity;
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
                ring.Size.Inner,
                ring.Size.Outer,
                palette,
                accent,
                onAccent);
            mainSectors.Add(sector);
            mainLayer.Children.Add(sector.Root);
        }
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
}
