// 创建时间：2026-09-12
// 功能：Linux 桌面的系统通知（notify-send / org.freedesktop.Notifications）
//       与 Dock 角标（com.canonical.Unity.LauncherEntry）。
// 目的：在 Ubuntu/GNOME 上为 Avalonia 前端补齐 WPF 版的通知与任务栏角标体验。

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PIHarness.Avalonia.Shell;

internal sealed class LinuxShell : IDisposable
{
    // 与 pi-harbor.desktop 同名，Dock 才能把角标贴到正确的图标上。
    private const string DesktopEntryId = "pi-harbor.desktop";
    private readonly object _badgeLock = new();
    private readonly object _notificationLock = new();
    private Process? _activeNotification;
    private (int Count, bool Urgent)? _pendingBadge;
    private (int Count, bool Urgent)? _lastBadge;
    private bool _badgeWorkerRunning;
    private bool _badgeUnavailable;
    private bool _disposed;
    private bool _notificationUnavailable;

    /// <summary>把未读数同步到 Dock 图标角标；urgent 用于让图标显示提醒状态。</summary>
    public void SetUnread(int nCount, bool bUrgent)
    {
        if (_disposed || _badgeUnavailable)
        {
            return;
        }

        var badge = (Math.Max(0, nCount), bUrgent);
        lock (_badgeLock)
        {
            // 未读数没变就不重复调用 DBus（激活窗口会连续触发多次属性变化）。
            if (_lastBadge == badge || _pendingBadge == badge)
            {
                return;
            }

            _pendingBadge = badge;
            if (_badgeWorkerRunning)
            {
                return;
            }

            _badgeWorkerRunning = true;
        }

        _ = Task.Run(FlushBadgeAsync);
    }

    private async Task FlushBadgeAsync()
    {
        while (true)
        {
            (int Count, bool Urgent) badge;
            lock (_badgeLock)
            {
                if (_pendingBadge is null || _disposed)
                {
                    _badgeWorkerRunning = false;
                    return;
                }

                badge = _pendingBadge.Value;
                _pendingBadge = null;
            }

            var bVisible = badge.Count > 0;
            var payload = $"{{'count': <uint32 {badge.Count}>, 'count-visible': <{(bVisible ? "true" : "false")}>, " +
                          $"'urgent': <{(badge.Urgent ? "true" : "false")}>}}";
            var bSent = await RunAsync("gdbus",
            [
                "call", "--session", "--dest", "com.canonical.Unity",
                "--object-path", "/com/canonical/Unity/LauncherEntry",
                "--method", "com.canonical.Unity.LauncherEntry.Update",
                $"application://{DesktopEntryId}", payload,
            ]).ConfigureAwait(false);
            lock (_badgeLock)
            {
                // Ubuntu 24.04 的 dock 不再实现 Unity LauncherEntry：探测一次失败后停止尝试。
                if (!bSent)
                {
                    _badgeUnavailable = true;
                    _badgeWorkerRunning = false;
                    return;
                }

                _lastBadge = badge;
            }
        }
    }

    /// <summary>发送系统通知；用户点击通知时在 UI 线程执行 onActivated。</summary>
    public void Notify(string title, string body, Action onActivated)
    {
        if (_disposed || _notificationUnavailable)
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "notify-send",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--app-name=Pi Harbor");
            startInfo.ArgumentList.Add("--icon=pi-harbor");
            startInfo.ArgumentList.Add("--urgency=normal");
            startInfo.ArgumentList.Add("--expire-time=8000");
            // 带等待与默认动作：用户点击通知正文时 stdout 会输出 default。
            startInfo.ArgumentList.Add("--wait");
            startInfo.ArgumentList.Add("--action=default=打开");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(title);
            startInfo.ArgumentList.Add(body);

            var process = Process.Start(startInfo);
            if (process is null)
            {
                _notificationUnavailable = true;
                return;
            }

            lock (_notificationLock)
            {
                // 同一时间只保留一条待处理通知，避免堆积的 notify-send 进程。
                _activeNotification?.Kill(entireProcessTree: true);
                _activeNotification = process;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    if (output.Contains("default", StringComparison.Ordinal))
                    {
                        onActivated();
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
                {
                    // 通知进程被新的通知替换或应用退出：忽略。
                }
                finally
                {
                    lock (_notificationLock)
                    {
                        if (ReferenceEquals(_activeNotification, process))
                        {
                            _activeNotification = null;
                        }
                    }

                    process.Dispose();
                }
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // 未安装 libnotify-bin：退化为只显示侧栏未读，不再反复尝试。
            _notificationUnavailable = true;
        }
    }

    public void Dispose()
    {
        lock (_badgeLock)
        {
            _disposed = true;
        }

        lock (_notificationLock)
        {
            try { _activeNotification?.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            _activeNotification = null;
        }

    }

    /// <summary>执行外部命令并返回是否成功（退出码 0）；缺失命令视为失败。</summary>
    private static async Task<bool> RunAsync(string fileName, string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            _ = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // 没有 gdbus 或桌面环境不支持 Unity LauncherEntry：角标降级为无。
            return false;
        }
    }
}
