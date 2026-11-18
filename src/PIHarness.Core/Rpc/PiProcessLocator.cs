// Created: 2026-09-06
// Purpose: Locate the installed pi command and create a redirected RPC process
//          (Windows: pi.cmd via cmd.exe; Linux/macOS: `pi` from PATH executed directly).

using System.Diagnostics;
using System.Text;

namespace PIHarness.Core.Rpc;

public static class PiProcessLocator
{
    /// <summary>Test hook: force the Unix discovery/launch branch on any OS.</summary>
    internal static bool ForceUnixMode = false;

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
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, ".npm-global", "bin", "pi"),
            Path.Combine(home, ".local", "bin", "pi"),
            Path.Combine(home, "node_modules", ".bin", "pi"),
            "/usr/local/bin/pi",
            "/usr/bin/pi",
        };

        // Unix PATH uses ':'; Path.PathSeparator keeps Windows drive letters intact in the test suite.
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(pathEntry.Trim('"'), "pi"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new PiLaunchResult(true, Path.GetFullPath(candidate), null);
            }
        }

        return new PiLaunchResult(false, null,
            "未找到 pi 命令。请确认 pi 已通过 npm 全局安装（npm i -g @earendil-works/pi-coding-agent）并可在终端中运行，或将其所在目录加入 PATH。");
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
