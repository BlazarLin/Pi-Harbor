// Created: 2026-09-06
// Purpose: Manage one owned pi RPC process with bounded diagnostics and correlated requests.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using PIHarness.Core.Models;

namespace PIHarness.Core.Rpc;

public sealed class PiRpcClient : IAsyncDisposable
{
    private const int MaxPendingRequests = 128;
    private const int MaxDiagnosticLines = 200;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<PiStartOptions, ProcessStartInfo> _startInfoFactory;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _diagnosticLock = new();
    private readonly Queue<string> _diagnostics = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Process? _process;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private long _nextRequestId;
    private bool _disposing;
    private bool _disposed;

    public PiRpcClient(Func<PiStartOptions, ProcessStartInfo>? startInfoFactory = null)
    {
        _startInfoFactory = startInfoFactory ?? CreateDefaultStartInfo;
    }

    public event EventHandler<PiRpcEvent>? EventReceived;
    public event EventHandler<string>? DiagnosticReceived;
    public event EventHandler<int>? Exited;

    public int ProcessId => _process?.Id ?? 0;
    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (_diagnosticLock)
            {
                return _diagnostics.ToArray();
            }
        }
    }

    public Task StartAsync(PiStartOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null)
        {
            throw new PiRpcException("pi RPC 进程已经启动。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var process = new Process
            {
                StartInfo = _startInfoFactory(options),
                EnableRaisingEvents = true,
            };
            process.Exited += OnProcessExited;
            if (!process.Start())
            {
                process.Dispose();
                throw new PiRpcException("启动 pi RPC 进程失败。");
            }

            _process = process;
            _stdoutTask = ReadStdoutAsync(process.StandardOutput, _lifetimeCts.Token);
            _stderrTask = ReadStderrAsync(process.StandardError, _lifetimeCts.Token);
            return Task.CompletedTask;
        }
        catch (PiRpcException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            throw new PiRpcException($"启动 pi RPC 进程失败：{exception.Message}", exception);
        }
    }

    public async Task<JsonElement> RequestAsync(
        string command,
        IReadOnlyDictionary<string, object?>? fields,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not { HasExited: false } process)
        {
            throw new PiRpcException("pi RPC 进程未运行或已经退出。");
        }

        if (_pendingRequests.Count >= MaxPendingRequests)
        {
            throw new PiRpcException("等待中的 RPC 请求过多，请稍后重试。");
        }

        var requestId = $"req-{Interlocked.Increment(ref _nextRequestId)}";
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(requestId, completion))
        {
            throw new PiRpcException("无法登记 RPC 请求。");
        }

        try
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = requestId,
                ["type"] = command,
            };
            if (fields is not null)
            {
                foreach (var pair in fields)
                {
                    payload[pair.Key] = pair.Value;
                }
            }

            var line = JsonSerializer.Serialize(payload);
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout ?? DefaultRequestTimeout);
            try
            {
                var response = await completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                if (response.TryGetProperty("success", out var success) && !success.GetBoolean())
                {
                    var error = response.TryGetProperty("error", out var errorElement)
                        ? errorElement.ToString()
                        : "pi 返回失败响应";
                    throw new PiRpcException($"RPC 命令 {command} 失败：{error}");
                }

                return response;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new PiRpcException($"RPC 命令 {command} 等待超时。");
            }
        }
        catch (IOException exception)
        {
            throw new PiRpcException($"发送 RPC 命令 {command} 失败：{exception.Message}", exception);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    public Task<JsonElement> RequestAsync(string command, CancellationToken cancellationToken) =>
        RequestAsync(command, null, null, cancellationToken);

    public Task<JsonElement> SendPromptAsync(string message, CancellationToken cancellationToken) =>
        SendPromptAsync(message, [], cancellationToken);

    public Task<JsonElement> SendPromptAsync(string message, IReadOnlyList<PromptImage> images, CancellationToken cancellationToken) =>
        RequestAsync(
            "prompt",
            images.Count == 0
                ? new Dictionary<string, object?> { ["message"] = message }
                : new Dictionary<string, object?> { ["message"] = message, ["images"] = images },
            DefaultRequestTimeout,
            cancellationToken);

    public async Task AbortAsync(CancellationToken cancellationToken)
    {
        await RequestAsync("clear_queue", null, DefaultRequestTimeout, cancellationToken).ConfigureAwait(false);
        await RequestAsync("abort", null, DefaultRequestTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposing = true;
        _disposed = true;
        var process = _process;
        if (process is not null)
        {
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                AddDiagnostic($"回收 pi RPC 进程失败：{exception.Message}");
            }
        }

        _lifetimeCts.Cancel();
        var readers = new[] { _stdoutTask, _stderrTask }.Where(task => task is not null).Cast<Task>().ToArray();
        try
        {
            await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
        }

        FailPendingRequests(new PiRpcException("pi RPC 客户端已经关闭。"));
        process?.Dispose();
        _process = null;
        _writeLock.Dispose();
        _lifetimeCts.Dispose();
    }

    private static ProcessStartInfo CreateDefaultStartInfo(PiStartOptions options)
    {
        var result = PiProcessLocator.Find();
        if (!result.Found || string.IsNullOrWhiteSpace(result.PiCommandPath))
        {
            throw new PiRpcException(result.ErrorMessage ?? "未找到 pi.cmd。");
        }

        return PiProcessLocator.CreateStartInfo(result.PiCommandPath, options);
    }

    private async Task ReadStdoutAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!RpcLineParser.TryParse(line, out var document) || document is null)
                {
                    AddDiagnostic(line);
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                    {
                        AddDiagnostic(line);
                        continue;
                    }

                    var type = typeElement.GetString() ?? string.Empty;
                    if (type == "response" &&
                        root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String &&
                        _pendingRequests.TryGetValue(idElement.GetString()!, out var completion))
                    {
                        completion.TrySetResult(root.Clone());
                    }
                    else
                    {
                        EventReceived?.Invoke(this, new PiRpcEvent(type, root.Clone()));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (!_disposing)
            {
                AddDiagnostic($"读取 pi RPC 输出失败：{exception.Message}");
            }
        }
    }

    private async Task ReadStderrAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                AddDiagnostic(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (!_disposing)
            {
                AddDiagnostic($"读取 pi RPC 错误输出失败：{exception.Message}");
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        var exitCode = 0;
        try
        {
            exitCode = sender is Process exitedProcess ? exitedProcess.ExitCode : 0;
        }
        catch (InvalidOperationException)
        {
        }

        if (!_disposing)
        {
            FailPendingRequests(new PiRpcException($"pi RPC 进程意外退出，退出码：{exitCode}。"));
        }

        Exited?.Invoke(this, exitCode);
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var completion in _pendingRequests.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private void AddDiagnostic(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_diagnosticLock)
        {
            while (_diagnostics.Count >= MaxDiagnosticLines)
            {
                _diagnostics.Dequeue();
            }

            _diagnostics.Enqueue(text);
        }

        DiagnosticReceived?.Invoke(this, text);
    }
}
