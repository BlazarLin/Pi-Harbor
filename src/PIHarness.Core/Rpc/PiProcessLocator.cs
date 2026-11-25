// Created: 2026-09-06
// Purpose: Locate the installed pi command and create a redirected RPC process
//          (Windows: pi.cmd via cmd.exe; Linux/macOS: `pi` from PATH executed directly).

using System.Diagnostics;
using System.Text;

namespace PIHarness.Core.Rpc;

public static class PiProcessLocator
{
    /// <summary>测试钩子：在任意平台强制走 Unix 发现/启动分支。</summary>
    internal static bool ForceUnixMode = false;

    /// <summary>测试钩子：替换 Unix 固定候选目录，null 表示使用默认列表。</summary>
    internal static string[]? UnixFallbackDirectoryOverride { get; set; }

    private static bool UseUnix => ForceUnixMode || !OperatingSystem.IsWindows();

    public static PiLaunchResult Find()
    {
        return UseUnix ? FindOnUnix() : FindOnWindows();
    }

    private static PiLaunchResult FindOnWindows()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var standardPath = Path.Combine(appData, "npm", "pi.cmd");
        if (File.Exists(standardPath))
        {
            return new PiLaunchResult(true, standardPath, null);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathEntry.Trim('"'), "pi.cmd");
            if (File.Exists(candidate))
            {
                return new PiLaunchResult(true, candidate, null);
            }
        }

        return new PiLaunchResult(false, null, "未找到 pi.cmd。请确认 pi 已通过 npm 全局安装，并且可在终端中运行。");
    }

    private static PiLaunchResult FindOnUnix()
    {
        // 固定安装目录优先：桌面启动时 PATH 往往只有系统目录，而 /usr/bin/pi 在
        // Debian/Ubuntu 上是 GNU 的圆周率计算工具，同名但绝不是 Pi 编码代理。
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallbackDirectories = UnixFallbackDirectoryOverride ??
        [
            Path.Combine(home, ".pi", "agent", "bin"),
            Path.Combine(home, ".npm-global", "bin"),
            Path.Combine(home, ".local", "bin"),
            Path.Combine(home, "node_modules", ".bin"),
            "/usr/local/bin",
            "/usr/bin",
        ];

        var candidates = new List<string>();
        foreach (var directory in fallbackDirectories)
        {
            candidates.Add(Path.Combine(directory, "pi"));
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(pathEntry.Trim('"'), "pi"));
        }

        var bSawForeignPi = false;
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            // 同名命令不一定是 Pi 编码代理：必须能输出版本号才算命中。
            if (IsPiAgentCommand(candidate))
            {
                return new PiLaunchResult(true, Path.GetFullPath(candidate), null);
            }

            bSawForeignPi = true;
        }

        var message = bSawForeignPi
            ? "找到的 pi 不是 Pi 编码代理（Ubuntu 的 /usr/bin/pi 是圆周率计算工具）。请确认 pi 已通过 npm 全局安装（npm i -g @earendil-works/pi-coding-agent）并可在终端中运行，或将其所在目录加入 PATH。"
            : "未找到 pi 命令。请确认 pi 已通过 npm 全局安装（npm i -g @earendil-works/pi-coding-agent）并可在终端中运行，或将其所在目录加入 PATH。";
        return new PiLaunchResult(false, null, message);
    }

    private static readonly Dictionary<string, (long Length, DateTime WriteTime, bool Valid)> ValidationCache = new(StringComparer.Ordinal);
    private static readonly object ValidationLock = new();

    /// <summary>执行 pi --version 判断是否为 Pi 编码代理；结果按文件路径/大小/修改时间缓存。</summary>
    private static bool IsPiAgentCommand(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        lock (ValidationLock)
        {
            if (ValidationCache.TryGetValue(path, out var cached) &&
                cached.Length == info.Length && cached.WriteTime == info.LastWriteTimeUtc)
            {
                return cached.Valid;
            }
        }

        var bValid = ProbePiVersion(path);
        lock (ValidationLock)
        {
            ValidationCache[path] = (info.Length, info.LastWriteTimeUtc, bValid);
        }

        return bValid;
    }

    private static bool ProbePiVersion(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                ArgumentList = { "--version" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            });
            if (process is null)
            {
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                return false;
            }

            var output = stdoutTask.GetAwaiter().GetResult();
            _ = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
            {
                return false;
            }

            // Pi 编码代理输出裸版本号（如 1.6.0）；GNU pi 输出 "pi (CLN 1.3.7)"。
            var firstLine = output.Split('\n', 2, StringSplitOptions.TrimEntries)[0];
            return Version.TryParse(firstLine, out _);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public static ProcessStartInfo CreateStartInfo(string piCommandPath, PiStartOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(piCommandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);

        return UseUnix ? CreateUnixStartInfo(piCommandPath, options) : CreateWindowsStartInfo(piCommandPath, options);
    }

    private static ProcessStartInfo CreateUnixStartInfo(string piCommandPath, PiStartOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = piCommandPath,
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("rpc");
        startInfo.ArgumentList.Add("--approve");
        if (options.NoSession)
        {
            startInfo.ArgumentList.Add("--no-session");
        }

        if (options.Offline)
        {
            startInfo.ArgumentList.Add("--offline");
        }

        if (!string.IsNullOrWhiteSpace(options.SessionPath))
        {
            startInfo.ArgumentList.Add("--session");
            startInfo.ArgumentList.Add(Path.GetFullPath(options.SessionPath));
        }

        if (!string.IsNullOrWhiteSpace(options.SessionDirectory))
        {
            startInfo.ArgumentList.Add("--session-dir");
            startInfo.ArgumentList.Add(Path.GetFullPath(options.SessionDirectory));
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateWindowsStartInfo(string piCommandPath, PiStartOptions options)
    {
        var command = new StringBuilder();
        command.Append(QuoteForCmd(piCommandPath));
        command.Append(" --mode rpc --approve");
        if (options.NoSession)
        {
            command.Append(" --no-session");
        }

        if (options.Offline)
        {
            command.Append(" --offline");
        }

        if (!string.IsNullOrWhiteSpace(options.SessionPath))
        {
            command.Append(" --session ");
            command.Append(QuoteForCmd(Path.GetFullPath(options.SessionPath)));
        }

        if (!string.IsNullOrWhiteSpace(options.SessionDirectory))
        {
            command.Append(" --session-dir ");
            command.Append(QuoteForCmd(Path.GetFullPath(options.SessionDirectory)));
        }

        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec))
        {
            comSpec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = comSpec,
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        startInfo.Arguments = $"/d /s /c \"{command}\"";
        return startInfo;
    }

    private static string QuoteForCmd(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("Windows 路径不能包含双引号。", nameof(value));
        }

        return $"\"{value.Replace("%", "%%", StringComparison.Ordinal)}\"";
    }
}
