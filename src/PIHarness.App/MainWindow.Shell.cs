// Created: 2026-09-08
// Purpose: Windows shell integration: tray icon, close-to-tray, global hotkey (Ctrl+Alt+P)
//          and window layout persistence.

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PIHarness.App.ViewModels;
using PIHarness.Core.Sessions;
using WinForms = System.Windows.Forms;

namespace PIHarness.App;

public partial class MainWindow
{
    private WinForms.NotifyIcon? _trayIcon;
    private HwndSource? _hotkeySource;
    private bool _forceExit;

    private const int HotKeyId = 0x5048;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x2;
    private const uint ModAlt = 0x1;
    private const uint VkP = 0x50;

    private bool IsQaMode => Environment.GetCommandLineArgs().Any(argument =>
        argument.StartsWith("--qa-", StringComparison.Ordinal) ||
        argument.StartsWith("--capture-", StringComparison.Ordinal));

    private void InitializeShell()
    {
        if (IsQaMode)
        {
            return;
        }

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "Pi Harbor — Pi Session Desk",
            Visible = true,
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // The tray tooltip still identifies the app without the extracted icon.
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitFromTray());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void ExitFromTray()
    {
        _forceExit = true;
        Close();
    }

    private void RegisterGlobalHotKey()
    {
        if (IsQaMode || _hotkeySource is not null)
        {
            return;
        }

        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        _hotkeySource = source;
        source.AddHook(WndProcHook);
        ApplyGlobalHotKeyRegistration();
    }

    private void ApplyGlobalHotKeyRegistration()
    {
        if (_hotkeySource is null)
        {
            return;
        }

        UnregisterHotKey(_hotkeySource.Handle, HotKeyId);
        if (_viewModel.GlobalHotkeyEnabled)
        {
            RegisterHotKey(_hotkeySource.Handle, HotKeyId, ModControl | ModAlt, VkP);
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotKeyId)
        {
            if (IsActive)
            {
                Hide();
            }
            else
            {
                ShowMainWindow();
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ReleaseShell()
    {
        if (_hotkeySource is not null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HotKeyId);
            _hotkeySource.RemoveHook(WndProcHook);
            _hotkeySource = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void RestoreWindowLayout()
    {
        var layout = _viewModel.WindowLayout;
        if (layout is null)
        {
            return;
        }

        if (layout.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            Left = Math.Clamp(layout.Left, 0, Math.Max(0, SystemParameters.VirtualScreenWidth - 200));
            Top = Math.Clamp(layout.Top, 0, Math.Max(0, SystemParameters.VirtualScreenHeight - 120));
            Width = Math.Clamp(layout.Width, MinWidth, SystemParameters.VirtualScreenWidth);
            Height = Math.Clamp(layout.Height, MinHeight, SystemParameters.VirtualScreenHeight);
        }

        if (layout.SidebarWidth is > 100 and < 900 && FindName("SidebarColumn") is ColumnDefinition column)
        {
            column.Width = new GridLength(layout.SidebarWidth);
        }
    }

    private WindowLayoutData? CaptureWindowLayout()
    {
        var sidebarWidth = (FindName("SidebarColumn") as ColumnDefinition)?.Width.Value ?? 300;
        return new WindowLayoutData(Left, Top, ActualWidth, ActualHeight,
            WindowState == WindowState.Maximized, sidebarWidth);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
