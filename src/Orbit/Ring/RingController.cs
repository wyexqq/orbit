using System.Windows;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Conversion;
using Orbit.Core.Formats;
using Orbit.Explorer;
using Orbit.Input;
using Orbit.Interop;
using Orbit.Settings;
using Orbit.Ui;

namespace Orbit.Ring;

internal enum PressOrigin
{
    ShellView,
    Elsewhere,
    RingCenter,
    RingSector,
}

internal sealed class Press(NativePoint start, double scale, PressOrigin origin, Surface surface)
{
    public NativePoint Start { get; } = start;

    public double Scale { get; } = scale;

    public PressOrigin Origin { get; } = origin;

    public Surface Surface { get; } = surface;

    public double Travel { get; set; }

    public bool Moved { get; set; }

    public bool IsOnRing => Origin is PressOrigin.RingCenter or PressOrigin.RingSector;
}

internal sealed class RingController
{
    private const double DragStartDistance = 6;
    private const double StickyTravelLimit = 8;
    private const double ArmTravel = 6;
    private const double WholeRingMargin = 12;
    private static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan ExtrasDwell = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan DropGrace = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan DragEnterWait = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan DoneFlash = TimeSpan.FromMilliseconds(280);

    private readonly AppSettings settings;
    private readonly InputHooks hooks;
    private readonly RingWindow window;
    private readonly ToastWindow toast;
    private readonly ConversionCoordinator coordinator;
    private readonly ShakeDetector shake = new();
    private readonly DispatcherTimer holdTimer;
    private readonly DispatcherTimer extrasTimer;
    private readonly DispatcherTimer dragEnterTimer;
    private RingModel? ring;
    private Press? press;
    private bool ringBusy;
    private bool awaitingDragEnter;
    private bool shakeUsedThisPress;

    public RingController(AppSettings settings, InputHooks hooks, RingWindow window, ToastWindow toast, ConversionCoordinator coordinator)
    {
        this.settings = settings;
        this.hooks = hooks;
        this.window = window;
        this.toast = toast;
        this.coordinator = coordinator;
        holdTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = HoldDelay };
        holdTimer.Tick += (_, _) => OnHoldElapsed();
        extrasTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = ExtrasDwell };
        extrasTimer.Tick += (_, _) => OnExtrasDwellElapsed();
        dragEnterTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = DragEnterWait };
        dragEnterTimer.Tick += (_, _) => OnDragEnterTimeout();

        hooks.LeftDown += OnLeftDown;
        hooks.LeftUp += OnLeftUp;
        hooks.Moved += OnMoved;
        hooks.HotkeyPressed += OnHotkey;
        hooks.DigitPressed += OnDigit;
        hooks.EscapePressed += CancelEverything;
        window.FilesDragged += OnFilesDragged;
        window.FilesDropped += OnFilesDropped;
        window.DropEffect = DecideDropEffect;
        ApplySettings();
    }

    public void ApplySettings()
    {
        hooks.HotkeyEnabled = settings.HotkeyEnabled;
        hooks.Paused = settings.Paused;
        if (settings.Paused)
        {
            CancelEverything();
        }
    }

    private void OnLeftDown(NativePoint point)
    {
        if (ringBusy)
        {
            return;
        }
        if (ring is not null && !awaitingDragEnter)
        {
            var hit = ring.HitAt(point);
            if (hit is not null)
            {
                var origin = hit.Value.Kind == HitKind.Center ? PressOrigin.RingCenter : PressOrigin.RingSector;
                press = new Press(point, ring.Scale, origin, default);
                ring.ButtonHeld = true;
                SyncKeyCapture();
                window.UpdateHitDisc();
                UpdateHover(point);
                return;
            }
        }
        if (ring is not null)
        {
            CloseRing();
        }
        var surface = ExplorerSurface.FileViewAt(point);
        var (_, scale) = NativeMethods.MonitorAt(point);
        press = new Press(point, scale, surface.IsShell ? PressOrigin.ShellView : PressOrigin.Elsewhere, surface);
        shake.Reset();
        shakeUsedThisPress = false;
        holdTimer.Stop();
        if (surface.IsShell && settings.HoldEnabled)
        {
            holdTimer.Start();
        }
    }

    private void OnMoved(NativePoint point)
    {
        if (press is not null && !NativeMethods.IsPrimaryButtonDown())
        {
            press = null;
            holdTimer.Stop();
        }
        if (press is not null)
        {
            TrackPress(press, point);
        }
        if (ring is not null)
        {
            ring.TravelSinceOpen = Math.Max(ring.TravelSinceOpen, ring.DistanceFromOpen(point));
            if (!ringBusy && !awaitingDragEnter)
            {
                UpdateHover(point);
            }
            return;
        }
        var isDraggingFromExplorer = press is { Moved: true, Origin: PressOrigin.ShellView };
        if (!isDraggingFromExplorer || !settings.ShakeEnabled || shakeUsedThisPress)
        {
            return;
        }
        if (ExplorerSurface.IsTaskbarAt(point))
        {
            shake.Reset();
            return;
        }
        if (shake.Feed(point, press!.Scale) && !NativeMethods.IsForegroundWindowMovingOrSizing())
        {
            shakeUsedThisPress = true;
            OpenAwaitingDrag(point);
        }
    }

    private void OnLeftUp(NativePoint point)
    {
        var releasedPress = press;
        press = null;
        holdTimer.Stop();
        if (ringBusy || ring is null)
        {
            return;
        }
        if (awaitingDragEnter)
        {
            CloseRing();
            return;
        }
        if (!ring.ButtonHeld)
        {
            return;
        }
        var keepOriginal = NativeMethods.IsKeyDown(NativeMethods.ControlKey);
        var releasedRing = ring;
        if (releasedPress is { IsOnRing: true })
        {
            FinishGesture(point, releasedPress, keepOriginal);
            return;
        }
        var grace = new DispatcherTimer(DispatcherPriority.Input) { Interval = DropGrace };
        grace.Tick += (_, _) =>
        {
            grace.Stop();
            if (ring == releasedRing && !ringBusy && ring.ButtonHeld)
            {
                FinishGesture(point, releasedPress, keepOriginal);
            }
        };
        grace.Start();
    }

    private void FinishGesture(NativePoint point, Press? releasedPress, bool keepOriginal)
    {
        if (ring is null)
        {
            return;
        }
        ring.ButtonHeld = false;
        window.UpdateHitDisc();
        UpdateHover(point);
        var target = ring.TargetOf(ring.Hovered);
        var justOpened = !ring.WasSticky && ring.TravelSinceOpen < ArmTravel;
        if (target is not null && target != RingModel.More && !justOpened)
        {
            StartConversion(target, keepOriginal);
            return;
        }
        if (target == RingModel.More)
        {
            if (!ring.ExtrasOpen)
            {
                OpenExtras();
            }
            MakeSticky();
            return;
        }
        var staysOpen = ring.Opener == RingOpener.Shake
            ? justOpened || ReleasedOnCenter(ring, releasedPress)
            : FilesStillInPlace(ring);
        if (staysOpen)
        {
            MakeSticky();
            return;
        }
        CloseRing();
    }

    private static bool ReleasedOnCenter(RingModel current, Press? releasedPress)
    {
        var releasedNearPress = releasedPress is null || releasedPress.Travel <= StickyTravelLimit;
        return current.Hovered is { Kind: HitKind.Center } && releasedNearPress;
    }

    private static bool FilesStillInPlace(RingModel current)
    {
        return current.Files.All(File.Exists);
    }

    private async void OnHoldElapsed()
    {
        holdTimer.Stop();
        var heldPress = press;
        if (heldPress is null || heldPress.Moved || ring is not null)
        {
            return;
        }
        var fileBox = await ItemUnderCursor.FindFileBoxAsync(heldPress.Start);
        if (fileBox is not NativeRect box || IsStale(heldPress))
        {
            return;
        }
        var files = await ExplorerSelection.ReadAsync(heldPress.Surface);
        if (IsStale(heldPress))
        {
            return;
        }
        var convertible = files.Where(FormatCatalog.IsConvertibleFile).ToList();
        if (convertible.Count == 0)
        {
            return;
        }
        OpenRing(PlaceAroundFile(box), heldPress.Start, convertible, RingOpener.Hold, buttonHeld: true);
    }

    private bool IsStale(Press heldPress)
    {
        return press != heldPress || heldPress.Moved || ring is not null || ringBusy;
    }

    private async void OnHotkey()
    {
        if (ringBusy)
        {
            return;
        }
        if (ring is not null)
        {
            CloseRing();
            return;
        }
        if (press is { Moved: true, IsOnRing: false })
        {
            shakeUsedThisPress = true;
            OpenAwaitingDrag(NativeMethods.CursorPosition());
            return;
        }
        var surface = ExplorerSurface.Foreground();
        if (!surface.IsShell)
        {
            return;
        }
        var files = await ExplorerSelection.ReadAsync(surface);
        var convertible = files.Where(FormatCatalog.IsConvertibleFile).ToList();
        if (ring is not null || ringBusy)
        {
            return;
        }
        if (convertible.Count == 0)
        {
            var message = files.Count == 0 ? "Сначала выделите файлы" : "Для этих файлов форматов нет";
            toast.ShowContent(new ToastContent(message, "Потом снова Alt+Q", AutoHideMilliseconds: 1800));
            return;
        }
        var anchor = HotkeyAnchor(surface);
        OpenRing(PlaceInsideScreen(anchor), anchor, convertible, RingOpener.Hotkey, buttonHeld: false);
    }

    private static NativePoint HotkeyAnchor(Surface surface)
    {
        var cursor = NativeMethods.CursorPosition();
        if (surface.Kind == SurfaceKind.Desktop)
        {
            return cursor;
        }
        NativeMethods.GetWindowRect(surface.RootWindow, out var bounds);
        if (bounds.Contains(cursor))
        {
            return cursor;
        }
        return new NativePoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    private void OnDigit(int number)
    {
        if (ring is null || ringBusy || ring.ButtonHeld)
        {
            return;
        }
        var index = number - 1;
        if (ring.DigitsOnExtras)
        {
            if (index >= ring.Extras.Count)
            {
                return;
            }
            ring.Hovered = new RingHit(HitKind.Extra, index, 0);
            window.Paint();
            StartConversion(ring.Extras[index], settings.KeepOriginals);
            return;
        }
        if (index >= ring.Main.Count)
        {
            return;
        }
        ring.Hovered = new RingHit(HitKind.Main, index, 0);
        if (ring.Main[index] == RingModel.More)
        {
            if (!ring.ExtrasOpen)
            {
                OpenExtras();
            }
            ring.DigitsOnExtras = true;
            window.Paint();
            return;
        }
        window.Paint();
        StartConversion(ring.Main[index], settings.KeepOriginals);
    }

    private void CancelEverything()
    {
        if (ringBusy)
        {
            return;
        }
        holdTimer.Stop();
        press = null;
        CloseRing();
    }

    private void OpenRing(RingPlace place, NativePoint openPoint, IReadOnlyList<string> files, RingOpener opener, bool buttonHeld)
    {
        var model = new RingModel(files, opener, buttonHeld, openPoint, place);
        if (!model.HasTargets)
        {
            toast.ShowContent(new ToastContent("Для этих файлов форматов нет", "Выделите файлы одного вида", AutoHideMilliseconds: 1800));
            return;
        }
        ring = model;
        awaitingDragEnter = false;
        window.Open(model, revealVisuals: true);
        hooks.RingVisible = true;
        SyncKeyCapture();
        UpdateHover(NativeMethods.CursorPosition());
    }

    private void OpenAwaitingDrag(NativePoint point)
    {
        var model = new RingModel([], RingOpener.Shake, buttonHeld: true, point, PlaceAt(point));
        ring = model;
        awaitingDragEnter = true;
        window.Open(model, revealVisuals: false);
        hooks.RingVisible = true;
        SyncKeyCapture();
        dragEnterTimer.Stop();
        dragEnterTimer.Start();
    }

    private void OnFilesDragged(IReadOnlyList<string> files)
    {
        if (ring is null || ringBusy)
        {
            return;
        }
        if (awaitingDragEnter)
        {
            awaitingDragEnter = false;
            dragEnterTimer.Stop();
            if (files.Count == 0)
            {
                CloseRingNow();
                return;
            }
            RevealWithFiles(files);
            return;
        }
        var differs = files.Count > 0 && !files.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(ring.Files);
        if (differs)
        {
            RevealWithFiles(files);
        }
    }

    private void RevealWithFiles(IReadOnlyList<string> files)
    {
        var previous = ring!;
        var model = new RingModel(files, previous.Opener, buttonHeld: true, previous.OpenPoint, previous.Place)
        {
            TravelSinceOpen = previous.TravelSinceOpen,
        };
        if (!model.HasTargets)
        {
            CloseRingNow();
            return;
        }
        ring = model;
        window.Reveal(model);
        SyncKeyCapture();
        UpdateHover(NativeMethods.CursorPosition());
    }

    private void OnDragEnterTimeout()
    {
        dragEnterTimer.Stop();
        if (awaitingDragEnter)
        {
            CloseRingNow();
        }
    }

    private DragDropEffects DecideDropEffect()
    {
        if (ring is null || ringBusy || awaitingDragEnter)
        {
            return DragDropEffects.None;
        }
        UpdateHover(NativeMethods.CursorPosition());
        var target = ring.TargetOf(ring.Hovered);
        return target is not null && target != RingModel.More ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnFilesDropped(bool keepOriginal)
    {
        if (ring is null || ringBusy)
        {
            return;
        }
        var target = ring.TargetOf(ring.Hovered);
        if (target is not null && target != RingModel.More)
        {
            StartConversion(target, keepOriginal);
        }
    }

    private void TrackPress(Press current, NativePoint point)
    {
        var dx = (point.X - current.Start.X) / current.Scale;
        var dy = (point.Y - current.Start.Y) / current.Scale;
        current.Travel = Math.Max(current.Travel, Math.Sqrt(dx * dx + dy * dy));
        if (current.Moved || current.Travel <= DragStartDistance)
        {
            return;
        }
        current.Moved = true;
        holdTimer.Stop();
    }

    private void UpdateHover(NativePoint point)
    {
        if (ring is null)
        {
            return;
        }
        ring.Hovered = ring.HitAt(point);
        SyncExtrasWithHover(ring);
        window.Paint();
    }

    private void SyncExtrasWithHover(RingModel current)
    {
        if (current.HoversMore)
        {
            var insideMainBand = current.Hovered!.Value.Distance <= current.Size.Outer + 4;
            if (insideMainBand && !current.ExtrasOpen && !extrasTimer.IsEnabled)
            {
                extrasTimer.Start();
            }
            return;
        }
        extrasTimer.Stop();
        if (current.Hovered is { Kind: HitKind.Main } && current.ExtrasOpen)
        {
            CloseExtras();
        }
    }

    private void OnExtrasDwellElapsed()
    {
        extrasTimer.Stop();
        if (ring is not null && ring.HoversMore && !ring.ExtrasOpen)
        {
            OpenExtras();
            window.Paint();
        }
    }

    private void OpenExtras()
    {
        extrasTimer.Stop();
        ring!.ExtrasOpen = true;
        window.ShowExtras();
    }

    private void CloseExtras()
    {
        ring!.ExtrasOpen = false;
        ring.DigitsOnExtras = false;
        window.HideExtras();
    }

    private void MakeSticky()
    {
        ring!.WasSticky = true;
        ring.ButtonHeld = false;
        window.UpdateHitDisc();
        SyncKeyCapture();
    }

    private void SyncKeyCapture()
    {
        hooks.RingTakesKeys = ring is not null && !ring.ButtonHeld && !ringBusy && !awaitingDragEnter;
    }

    private void CloseRing()
    {
        ResetRingState();
        window.CloseRing();
    }

    private void CloseRingNow()
    {
        ResetRingState();
        window.HideNow();
    }

    private void ResetRingState()
    {
        extrasTimer.Stop();
        dragEnterTimer.Stop();
        ring = null;
        awaitingDragEnter = false;
        hooks.RingVisible = false;
        hooks.RingTakesKeys = false;
    }

    private static RingPlace PlaceAroundFile(NativeRect fileBox)
    {
        var center = new NativePoint(fileBox.Left + fileBox.Width / 2, fileBox.Top + fileBox.Height / 2);
        var (workArea, scale) = NativeMethods.MonitorAt(center);
        var halfDiagonal = Math.Sqrt(fileBox.Width * fileBox.Width + fileBox.Height * fileBox.Height) / 2 / scale;
        var size = RingSize.AroundHole(halfDiagonal + RingGeometry.HolePadding);
        return new RingPlace(center, scale, workArea, size);
    }

    private static RingPlace PlaceAt(NativePoint point)
    {
        var (workArea, scale) = NativeMethods.MonitorAt(point);
        return new RingPlace(point, scale, workArea, RingSize.Standard);
    }

    private static RingPlace PlaceInsideScreen(NativePoint point)
    {
        var (workArea, scale) = NativeMethods.MonitorAt(point);
        var size = RingSize.Standard;
        var reach = (size.Outer + WholeRingMargin) * scale;
        var x = RingGeometry.ClampBetween(point.X, workArea.Left + reach, workArea.Right - reach);
        var y = RingGeometry.ClampBetween(point.Y, workArea.Top + reach, workArea.Bottom - reach);
        var center = new NativePoint((int)Math.Round(x), (int)Math.Round(y));
        return new RingPlace(center, scale, workArea, size);
    }

    private async void StartConversion(string target, bool keepOriginal)
    {
        var current = ring;
        if (current is null || ringBusy)
        {
            return;
        }
        var paths = current.Files.Where(path => FormatCatalog.ExtensionOf(path) != target).ToList();
        if (paths.Count == 0)
        {
            CloseRing();
            return;
        }
        ringBusy = true;
        SyncKeyCapture();
        extrasTimer.Stop();
        window.ShowWorking(0);
        var request = new ConversionRequest(paths, target, keepOriginal || settings.KeepOriginals);
        try
        {
            await coordinator.RunAsync(
                request,
                current.Accent,
                fraction =>
                {
                    if (ring == current)
                    {
                        window.ShowWorking(fraction);
                    }
                },
                async () =>
                {
                    window.ShowDone();
                    await Task.Delay(DoneFlash);
                    ReleaseRing(current);
                },
                () => ReleaseRing(current));
        }
        catch (Exception error)
        {
            Log.Error("Конвертация упала", error);
            ReleaseRing(current);
            toast.ShowContent(new ToastContent("Что-то пошло не так", error.Message, AutoHideMilliseconds: 6000));
        }
    }

    private void ReleaseRing(RingModel finished)
    {
        if (ring != finished)
        {
            return;
        }
        ringBusy = false;
        CloseRing();
    }
}
