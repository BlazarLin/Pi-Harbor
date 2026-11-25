// 创建时间：2026-09-12
// 功能：在 X11 上监听全局热键 Ctrl+Alt+P，按下时回调给主窗口。
// 目的：替代 Windows 的 RegisterHotKey；Wayland 原生会话不支持时自动降级。

using System;
using Avalonia.Threading;

namespace PIHarness.Avalonia.Shell;

/// <summary>
/// 全局热键（仅 X11）。所有 Xlib 调用都留在 UI 线程：Avalonia 的 X11 后端同样在 UI 线程使用
/// Xlib，跨线程另开 Display 会静默丢失请求。这里改为 50 ms 轮询键盘状态，
/// 避免被动抓取（XGrabKey）在部分窗口管理器/输入法组合下失效。
/// </summary>
internal sealed class X11Hotkey : IDisposable
{
    private readonly IntPtr _display;
    private readonly int[] _ctrlKeyCodes;
    private readonly int[] _altKeyCodes;
    private readonly int _letterKeyCode;
    private readonly DispatcherTimer _timer;
    private readonly Action _onPressed;
    private readonly byte[] _keyState = new byte[32];
    private bool _bPressed;
    private bool _bFaulted;
    private bool _disposed;

    private X11Hotkey(IntPtr display, int[] ctrlKeyCodes, int[] altKeyCodes, int letterKeyCode, Action onPressed)
    {
        _display = display;
        _ctrlKeyCodes = ctrlKeyCodes;
        _altKeyCodes = altKeyCodes;
        _letterKeyCode = letterKeyCode;
        _onPressed = onPressed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>启动成功返回实例，失败（Wayland、缺少 libX11）返回 null。</summary>
    public static X11Hotkey? TryStart(Action onPressed)
    {
        try
        {
            var display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return null;
            }

            var letterKeyCode = X11Native.XKeysymToKeycode(display, X11Native.XStringToKeysym("p"));
            if (letterKeyCode == 0)
            {
                X11Native.XCloseDisplay(display);
                return null;
            }

            var ctrlKeyCodes = ResolveKeyCodes(display, "Control_L", "Control_R");
            var altKeyCodes = ResolveKeyCodes(display, "Alt_L", "Alt_R");
            var hotkey = new X11Hotkey(display, ctrlKeyCodes, altKeyCodes, letterKeyCode, onPressed);
            hotkey._timer.Start();
            return hotkey;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static int[] ResolveKeyCodes(IntPtr display, string leftName, string rightName)
    {
        var left = X11Native.XKeysymToKeycode(display, X11Native.XStringToKeysym(leftName));
        var right = X11Native.XKeysymToKeycode(display, X11Native.XStringToKeysym(rightName));
        return left == right ? [left] : [left, right];
    }

    private void Poll()
    {
        if (_disposed || _bFaulted)
        {
            return;
        }

        try
        {
            if (X11Native.XQueryKeymap(_display, _keyState) == 0)
            {
                return;
            }

            var bDown = AnyDown(_ctrlKeyCodes) && AnyDown(_altKeyCodes) && IsDown(_letterKeyCode);
            if (bDown && !_bPressed)
            {
                _onPressed();
            }

            _bPressed = bDown;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            // X 连接断开（例如会话切换）：停止轮询，避免每个 tick 抛异常。
            _bFaulted = true;
            _timer.Stop();
        }
    }

    private bool AnyDown(int[] keyCodes)
    {
        foreach (var keyCode in keyCodes)
        {
            if (IsDown(keyCode))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDown(int keyCode) =>
        keyCode is >= 0 and < 256 && (_keyState[keyCode >> 3] & (1 << (keyCode & 7))) != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        try
        {
            X11Native.XCloseDisplay(_display);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // 退出阶段忽略释放失败。
        }
    }
}
