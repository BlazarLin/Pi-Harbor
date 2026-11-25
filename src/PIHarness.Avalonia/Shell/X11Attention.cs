// 创建时间：2026-09-12
// 功能：通过 X11 ClientMessage 给窗口加 _NET_WM_STATE_DEMANDS_ATTENTION。
// 目的：替代 Windows 的 FlashWindowEx，让 Dock 在窗口不在前台时提示新结果。

using System;

namespace PIHarness.Avalonia.Shell;

internal sealed class X11Attention : IDisposable
{
    private readonly IntPtr _display;
    private readonly ulong _root;
    private readonly ulong _netWmState;
    private readonly ulong _demandsAttention;
    private bool _disposed;

    private X11Attention(IntPtr display, ulong root, ulong netWmState, ulong demandsAttention)
    {
        _display = display;
        _root = root;
        _netWmState = netWmState;
        _demandsAttention = demandsAttention;
    }

    /// <summary>非 X11 会话或缺少 libX11 时返回 null，调用方直接降级。</summary>
    public static X11Attention? TryOpen()
    {
        try
        {
            var display = X11Native.XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return null;
            }

            var root = X11Native.XDefaultRootWindow(display);
            var netWmState = X11Native.XInternAtom(display, "_NET_WM_STATE", false);
            var demandsAttention = X11Native.XInternAtom(display, "_NET_WM_STATE_DEMANDS_ATTENTION", false);
            return new X11Attention(display, root, netWmState, demandsAttention);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void RequestAttention(IntPtr window)
    {
        if (_disposed || _display == IntPtr.Zero || window == IntPtr.Zero)
        {
            return;
        }

        var xEvent = new X11Native.XEvent
        {
            Type = 33, // ClientMessage
            Window = window,
            MessageType = (IntPtr)(long)_netWmState,
            Format = 32,
            Data0 = 1, // _NET_WM_STATE_ADD
            Data1 = (IntPtr)(long)_demandsAttention,
            Data2 = 0,
            Data3 = 1, // 来源：应用
            Data4 = 0,
        };
        // 必须发给根窗口，Window Manager 才会处理 _NET_WM_STATE。
        X11Native.XSendEvent(_display, _root, false,
            X11Native.SubstructureRedirectMask | X11Native.SubstructureNotifyMask, ref xEvent);
        X11Native.XFlush(_display);
    }

    /// <summary>把输入焦点交还根窗口，供冒烟脚本验证窗口不在前台时的紧急提示。</summary>
    public void ReleaseFocus()
    {
        if (_disposed || _display == IntPtr.Zero)
        {
            return;
        }

        // RevertToPointerRoot = 1, CurrentTime = 0
        X11Native.XSetInputFocus(_display, _root, 1, 0);
        X11Native.XFlush(_display);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        X11Native.XCloseDisplay(_display);
    }
}
