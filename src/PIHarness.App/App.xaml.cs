// Created: 2026-09-06
// Purpose: Own the WPF application lifetime for Pi Harbor.

using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;

namespace PIHarness.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--uninstall-notifications", StringComparer.Ordinal))
        {
            StartupUri = null;
            try { ToastNotificationManagerCompat.Uninstall(); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
