// Created: 2026-09-06
// Purpose: Locate the installed pi command and create a redirected Windows RPC process.

using System.Diagnostics;
using System.Text;

namespace PIHarness.Core.Rpc;

public static class PiProcessLocator
{
    public static PiLaunchResult Find()
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

    public static ProcessStartInfo CreateStartInfo(string piCommandPath, PiStartOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(piCommandPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WorkingDirectory);

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
