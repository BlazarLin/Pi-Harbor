// 创建时间：2026-09-12
// 功能：libX11 最小 P/Invoke 声明与 XEvent 内存布局。
// 目的：为窗口紧急提示与全局热键提供 X11 能力，避免引入额外原生依赖。

using System;
using System.Runtime.InteropServices;

namespace PIHarness.Avalonia.Shell;

internal static class X11Native
{
    public const int KeyPressType = 2;
    public const long SubstructureRedirectMask = 0x100000;
    public const long SubstructureNotifyMask = 0x80000;
    public const long KeyPressMask = 0x1;
    public const int GrabModeAsync = 1;
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3;
    public const uint LockMask = 1 << 1;
    public const uint Mod2Mask = 1 << 4;

    /// <summary>XEvent 是 192 字节的联合体；只使用 ClientMessage / KeyPress 字段。</summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    public struct XEvent
    {
        public int Type;
        public IntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Window;
        public IntPtr MessageType;
        public int Format;
        public IntPtr Data0;
        public IntPtr Data1;
        public IntPtr Data2;
        public IntPtr Data3;
        public IntPtr Data4;
    }

    [DllImport("libX11", EntryPoint = "XOpenDisplay")]
    public static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11", EntryPoint = "XCloseDisplay")]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11", EntryPoint = "XDefaultRootWindow")]
    public static extern ulong XDefaultRootWindow(IntPtr display);

    [DllImport("libX11", EntryPoint = "XInternAtom")]
    public static extern ulong XInternAtom(IntPtr display, string atomName, bool bOnlyIfExists);

    [DllImport("libX11", EntryPoint = "XSendEvent")]
    public static extern int XSendEvent(IntPtr display, ulong window, bool bPropagate, long eventMask, ref XEvent xEvent);

    [DllImport("libX11", EntryPoint = "XFlush")]
    public static extern int XFlush(IntPtr display);

    [DllImport("libX11", EntryPoint = "XCreateSimpleWindow")]
    public static extern ulong XCreateSimpleWindow(IntPtr display, ulong parent, int x, int y, uint width, uint height,
        uint borderWidth, ulong border, ulong background);

    [DllImport("libX11", EntryPoint = "XMapWindow")]
    public static extern int XMapWindow(IntPtr display, ulong window);

    [DllImport("libX11", EntryPoint = "XDestroyWindow")]
    public static extern int XDestroyWindow(IntPtr display, ulong window);

    [DllImport("libX11", EntryPoint = "XSelectInput")]
    public static extern int XSelectInput(IntPtr display, ulong window, long eventMask);

    [DllImport("libX11", EntryPoint = "XGrabKey")]
    public static extern int XGrabKey(IntPtr display, int keyCode, uint modifiers, ulong grabWindow,
        bool bOwnerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11", EntryPoint = "XUngrabKey")]
    public static extern int XUngrabKey(IntPtr display, int keyCode, uint modifiers, ulong grabWindow);

    [DllImport("libX11", EntryPoint = "XStringToKeysym")]
    public static extern ulong XStringToKeysym(string keyName);

    [DllImport("libX11", EntryPoint = "XKeysymToKeycode")]
    public static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport("libX11", EntryPoint = "XQueryKeymap")]
    public static extern int XQueryKeymap(IntPtr display, byte[] keys);

    [DllImport("libX11", EntryPoint = "XPending")]
    public static extern int XPending(IntPtr display);

    [DllImport("libX11", EntryPoint = "XNextEvent")]
    public static extern int XNextEvent(IntPtr display, out XEvent xEvent);

    [DllImport("libX11", EntryPoint = "XSetInputFocus")]
    public static extern int XSetInputFocus(IntPtr display, ulong focus, int revertTo, ulong time);

    [DllImport("libX11", EntryPoint = "XSync")]
    public static extern int XSync(IntPtr display, bool bDiscard);
}
