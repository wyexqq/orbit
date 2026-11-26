using System.Runtime.InteropServices;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Explorer;
using Orbit.Interop;

namespace Orbit.Input;

internal sealed class InputHooks : IDisposable
{
    private const int EscapeKey = 0x1B;
    private const int QKey = 0x51;
    private const int FirstDigitKey = 0x31;
    private const int LastDigitKey = 0x39;
    private const int FirstNumpadDigitKey = 0x61;
    private const int LastNumpadDigitKey = 0x69;

    private readonly Dispatcher dispatcher;
    private readonly HookProcedure mouseProcedure;
    private readonly HookProcedure keyboardProcedure;
    private readonly HashSet<uint> swallowedKeys = [];
    private readonly Thread thread;
    private readonly object moveGate = new();
    private NativePoint latestMove;
    private bool moveQueued;
    private uint threadId;
    private IntPtr mouseHook;
    private IntPtr keyboardHook;

    public InputHooks(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        mouseProcedure = OnMouse;
        keyboardProcedure = OnKeyboard;
        thread = new Thread(RunHookLoop)
        {
            IsBackground = true,
            Name = "Orbit input hooks",
            Priority = ThreadPriority.AboveNormal,
        };
    }

    public event Action<NativePoint>? LeftDown;

    public event Action<NativePoint>? LeftUp;

    public event Action<NativePoint>? Moved;

    public event Action? HotkeyPressed;

    public event Action<int>? DigitPressed;

    public event Action? EscapePressed;

    public volatile bool HotkeyEnabled = true;

    public volatile bool RingTakesKeys;

    public volatile bool RingVisible;

    public volatile bool Paused;

    public void Start()
    {
        thread.Start();
    }

    public void Dispose()
    {
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.QuitMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void RunHookLoop()
    {
        threadId = NativeMethods.GetCurrentThreadId();
        var module = NativeMethods.GetModuleHandle(null);
        mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.MouseLowLevelHook, mouseProcedure, module, 0);
        keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.KeyboardLowLevelHook, keyboardProcedure, module, 0);
        if (mouseHook == IntPtr.Zero || keyboardHook == IntPtr.Zero)
        {
            Log.Info($"Хуки ввода не поставились, код {Marshal.GetLastWin32Error()}");
        }
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }
        NativeMethods.UnhookWindowsHookEx(mouseHook);
        NativeMethods.UnhookWindowsHookEx(keyboardHook);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !Paused)
        {
            var info = Marshal.PtrToStructure<LowLevelMouseInfo>(lParam);
            switch (wParam.ToInt32())
            {
                case NativeMethods.MouseMove:
                    QueueMove(info.Point);
                    break;
                case NativeMethods.LeftButtonDown:
                    Raise(() => LeftDown?.Invoke(info.Point));
                    break;
                case NativeMethods.LeftButtonUp:
                    Raise(() => LeftUp?.Invoke(info.Point));
                    break;
            }
        }
        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || Paused)
        {
            return NativeMethods.CallNextHookEx(keyboardHook, code, wParam, lParam);
        }
        var info = Marshal.PtrToStructure<LowLevelKeyboardInfo>(lParam);
        var message = wParam.ToInt32();
        var isKeyDown = message is NativeMethods.KeyDown or NativeMethods.SystemKeyDown;
        var isKeyUp = message is NativeMethods.KeyUp or NativeMethods.SystemKeyUp;
        var isInjected = (info.Flags & NativeMethods.InjectedKeyFlag) != 0;
        if (isKeyUp && swallowedKeys.Remove(info.VirtualKey))
        {
            return new IntPtr(1);
        }
        if (isKeyDown && !isInjected && ShouldSwallow(info))
        {
            swallowedKeys.Add(info.VirtualKey);
            return new IntPtr(1);
        }
        if (isKeyDown && info.VirtualKey == EscapeKey && RingVisible)
        {
            Raise(() => EscapePressed?.Invoke());
        }
        return NativeMethods.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private bool ShouldSwallow(LowLevelKeyboardInfo info)
    {
        var key = (int)info.VirtualKey;
        var altHeld = (info.Flags & NativeMethods.AltDownFlag) != 0;
        if (key == QKey && altHeld && HotkeyEnabled && (RingVisible || ExplorerSurface.IsShellForeground()))
        {
            NativeMethods.TapMenuMaskKey();
            Raise(() => HotkeyPressed?.Invoke());
            return true;
        }
        if (!RingTakesKeys || altHeld)
        {
            return false;
        }
        if (key == EscapeKey)
        {
            Raise(() => EscapePressed?.Invoke());
            return true;
        }
        var digit = DigitOf(key);
        if (digit > 0)
        {
            Raise(() => DigitPressed?.Invoke(digit));
            return true;
        }
        return false;
    }

    private static int DigitOf(int key)
    {
        if (key is >= FirstDigitKey and <= LastDigitKey)
        {
            return key - FirstDigitKey + 1;
        }
        if (key is >= FirstNumpadDigitKey and <= LastNumpadDigitKey)
        {
            return key - FirstNumpadDigitKey + 1;
        }
        return 0;
    }

    private void QueueMove(NativePoint point)
    {
        lock (moveGate)
        {
            latestMove = point;
            if (moveQueued)
            {
                return;
            }
            moveQueued = true;
        }
        Raise(DeliverMove);
    }

    private void DeliverMove()
    {
        NativePoint point;
        lock (moveGate)
        {
            point = latestMove;
            moveQueued = false;
        }
        Moved?.Invoke(point);
    }

    private void Raise(Action action)
    {
        dispatcher.BeginInvoke(DispatcherPriority.Input, action);
    }
}
