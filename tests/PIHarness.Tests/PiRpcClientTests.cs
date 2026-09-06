// Created: 2026-09-06
// Purpose: Verify request correlation, timeout, abort order, and owned-process cleanup.

using System.Diagnostics;
using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class PiRpcClientTests
{
    [TestCase("TEST-05B", "RPC 客户端跨过普通日志并完成有效请求")]
    public static async Task CompletesRequestAfterDiagnosticAsync()
    {
        await using var client = CreateFakeClient();
        var diagnostics = new List<string>();
        client.DiagnosticReceived += (_, text) => diagnostics.Add(text);
        await client.StartAsync(new PiStartOptions(Environment.CurrentDirectory, NoSession: true), CancellationToken.None);

        var response = await client.RequestAsync("get_state", null, TimeSpan.FromSeconds(2), CancellationToken.None);

        AssertEx.True(response.GetProperty("success").GetBoolean(), "get_state 应成功");
        AssertEx.True(diagnostics.Any(text => text.Contains("dashboard", StringComparison.Ordinal)), "普通日志应进入诊断通道");
    }

    [TestCase("TEST-10", "中止顺序为 clear_queue 后 abort")]
    public static async Task ClearsQueueBeforeAbortAsync()
    {
        await using var client = CreateFakeClient();
        var orderSignal = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += (_, rpcEvent) =>
        {
            if (rpcEvent.Type == "test_command_order")
            {
                orderSignal.TrySetResult(rpcEvent.Payload.GetProperty("commands").EnumerateArray().Select(item => item.GetString()!).ToArray());
            }
        };
        await client.StartAsync(new PiStartOptions(Environment.CurrentDirectory, NoSession: true), CancellationToken.None);

        await client.AbortAsync(CancellationToken.None);
        var commands = await orderSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        AssertEx.True(commands.Length >= 2, "伪 RPC 应收到两条中止命令");
        AssertEx.Equal("clear_queue", commands[^2], "必须先清空队列");
        AssertEx.Equal("abort", commands[^1], "清空队列后才能中止");
    }

    [TestCase("TEST-12A", "RPC 请求超时返回中文错误")]
    public static async Task ReportsTimeoutInChineseAsync()
    {
        await using var client = CreateFakeClient();
        await client.StartAsync(new PiStartOptions(Environment.CurrentDirectory, NoSession: true), CancellationToken.None);

        var exception = await AssertEx.ThrowsAsync<PiRpcException>(
            () => client.RequestAsync("never", null, TimeSpan.FromMilliseconds(100), CancellationToken.None),
            "无响应请求应超时");

        AssertEx.True(exception.Message.Contains("超时", StringComparison.Ordinal), "超时错误应使用中文");
    }

    [TestCase("TEST-12B", "RPC 进程意外退出会终止等待请求")]
    public static async Task ReportsUnexpectedExitAsync()
    {
        await using var client = CreateFakeClient();
        await client.StartAsync(new PiStartOptions(Environment.CurrentDirectory, NoSession: true), CancellationToken.None);

        var exception = await AssertEx.ThrowsAsync<PiRpcException>(
            () => client.RequestAsync("exit", null, TimeSpan.FromSeconds(3), CancellationToken.None),
            "进程退出应终止请求");

        AssertEx.True(exception.Message.Contains("退出", StringComparison.Ordinal), "进程退出错误应使用中文");
    }

    [TestCase("TEST-12C", "RPC 程序缺失时返回中文错误")]
    public static async Task ReportsMissingExecutableAsync()
    {
        await using var client = new PiRpcClient(_ => new ProcessStartInfo
        {
            FileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
        });

        var exception = await AssertEx.ThrowsAsync<PiRpcException>(
            () => client.StartAsync(new PiStartOptions(Environment.CurrentDirectory), CancellationToken.None),
            "缺失程序应启动失败");

        AssertEx.True(exception.Message.Contains("启动", StringComparison.Ordinal), "启动错误应使用中文");
    }

    [TestCase("TEST-14", "释放 RPC 客户端后子进程退出")]
    public static async Task DisposesOwnedProcessAsync()
    {
        var client = CreateFakeClient();
        await client.StartAsync(new PiStartOptions(Environment.CurrentDirectory, NoSession: true), CancellationToken.None);
        var nProcessId = client.ProcessId;

        await client.DisposeAsync();
        await Task.Delay(100);

        AssertEx.False(IsProcessRunning(nProcessId), "释放后不应残留伪 RPC 进程");
    }

    private static PiRpcClient CreateFakeClient()
    {
        return new PiRpcClient(options =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                WorkingDirectory = options.WorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("--fake-rpc");
            return startInfo;
        });
    }

    private static bool IsProcessRunning(int nProcessId)
    {
        try
        {
            using var process = Process.GetProcessById(nProcessId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
