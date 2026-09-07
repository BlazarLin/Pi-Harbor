// Created: 2026-09-06
// Purpose: Run PI-Harness tests without third-party test packages.

using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using PIHarness.Core.Rpc;
using PIHarness.Core.Sessions;

namespace PIHarness.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--fake-rpc", StringComparer.Ordinal))
        {
            return await RunFakeRpcAsync().ConfigureAwait(false);
        }

        TestContext.Arguments = args;
        var testMethods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<TestCaseAttribute>()))
            .Where(item => item.Attribute is not null)
            .OrderBy(item => item.Attribute!.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Method.Name, StringComparer.Ordinal)
            .ToArray();

        var nFailed = 0;
        foreach (var item in testMethods)
        {
            try
            {
                var result = item.Method.Invoke(null, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }

                Console.WriteLine($"[通过] {item.Attribute!.Id}：{item.Attribute.Goal}");
            }
            catch (Exception exception)
            {
                nFailed++;
                var actual = exception is TargetInvocationException { InnerException: not null }
                    ? exception.InnerException
                    : exception;
                Console.WriteLine($"[失败] {item.Attribute!.Id}：{item.Attribute.Goal}");
                Console.WriteLine($"        {actual.GetType().Name}: {actual.Message}");
            }
        }

        var liveSessionRoot = ReadOption(args, "--live-session-root");
        if (!string.IsNullOrWhiteSpace(liveSessionRoot))
        {
            try
            {
                var timer = Stopwatch.StartNew();
                var snapshot = await SessionCatalog.ScanAsync(liveSessionRoot, CancellationToken.None).ConfigureAwait(false);
                timer.Stop();
                Console.WriteLine(
                    $"[实机只读] 项目 {snapshot.Projects.Count}，有效会话 {snapshot.SessionCount}，" +
                    $"跳过 {snapshot.SkippedFileCount}，耗时 {timer.ElapsedMilliseconds} ms");
                foreach (var warning in snapshot.Warnings.Take(5))
                {
                    Console.WriteLine($"[实机警告] {warning}");
                }
            }
            catch (Exception exception)
            {
                nFailed++;
                Console.WriteLine($"[实机失败] 会话目录只读扫描：{exception.Message}");
            }
        }

        var liveHistoryPath = ReadOption(args, "--live-history-probe");
        if (!string.IsNullOrWhiteSpace(liveHistoryPath))
        {
            try
            {
                var timer = Stopwatch.StartNew();
                var snapshot = await SessionHistoryReader.ReadAsync(liveHistoryPath, CancellationToken.None).ConfigureAwait(false);
                timer.Stop();
                var fileSizeMb = Math.Round(new FileInfo(liveHistoryPath).Length / 1024d / 1024d, 2);
                Console.WriteLine(
                    $"[实机会话] 文件 {fileSizeMb} MB，条目 {snapshot.EntryCount}，" +
                    $"显示项 {snapshot.Items.Count}，警告 {snapshot.Warnings.Count}，耗时 {timer.ElapsedMilliseconds} ms");
            }
            catch (Exception exception)
            {
                nFailed++;
                Console.WriteLine($"[实机失败] 本地轻量历史读取：{exception.Message}");
            }
        }

        if (args.Contains("--live-pi-probe", StringComparer.Ordinal))
        {
            try
            {
                await using var client = new PiRpcClient();
                client.DiagnosticReceived += (_, text) => Console.WriteLine($"[pi 诊断] {text}");
                await client.StartAsync(
                    new PiStartOptions(Environment.CurrentDirectory, NoSession: true, Offline: true),
                    CancellationToken.None).ConfigureAwait(false);
                var response = await client.RequestAsync(
                    "get_state",
                    null,
                    TimeSpan.FromSeconds(20),
                    CancellationToken.None).ConfigureAwait(false);
                var data = response.GetProperty("data");
                var sessionId = data.GetProperty("sessionId").GetString();
                var model = data.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.Object
                    ? modelElement.GetProperty("id").GetString()
                    : "未配置";
                Console.WriteLine($"[实机只读] pi RPC get_state 成功，会话 {sessionId}，模型 {model}");
            }
            catch (Exception exception)
            {
                nFailed++;
                Console.WriteLine($"[实机失败] pi RPC 只读探测：{exception.Message}");
            }
        }

        var liveChatProject = ReadOption(args, "--live-chat-probe");
        if (!string.IsNullOrWhiteSpace(liveChatProject))
        {
            try
            {
                var liveModel = ReadOption(args, "--live-model") ?? "ark/glm-5.3";
                await RunLiveChatProbeAsync(liveChatProject, liveModel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                nFailed++;
                Console.WriteLine($"[实机失败] pi 新建与继续会话：{exception}");
            }
        }

        Console.WriteLine($"测试完成：总计 {testMethods.Length}，通过 {testMethods.Length - nFailed}，失败 {nFailed}");
        return nFailed == 0 ? 0 : 1;
    }

    private static async Task<int> RunFakeRpcAsync()
    {
        var receivedCommands = new List<string>();
        Console.Out.WriteLine("[dashboard] fake non-json startup log");
        Console.Out.Flush();

        while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var id = root.GetProperty("id").GetString();
            var command = root.GetProperty("type").GetString() ?? string.Empty;
            receivedCommands.Add(command);
            var commandLog = Environment.GetEnvironmentVariable("PI_HARNESS_FAKE_COMMAND_LOG");
            if (!string.IsNullOrWhiteSpace(commandLog))
            {
                await File.AppendAllTextAsync(commandLog, command + Environment.NewLine).ConfigureAwait(false);
            }

            if (command == "get_state" &&
                int.TryParse(Environment.GetEnvironmentVariable("PI_HARNESS_FAKE_STATE_DELAY_MS"), out var nStateDelayMs) &&
                nStateDelayMs > 0)
            {
                await Task.Delay(nStateDelayMs).ConfigureAwait(false);
            }

            if (command == "never")
            {
                continue;
            }

            if (command == "exit")
            {
                return 7;
            }

            object response = command switch
            {
                "get_state" => new
                {
                    id,
                    type = "response",
                    command,
                    success = true,
                    data = new
                    {
                        isStreaming = false,
                        sessionId = "fake-session",
                        sessionName = "伪会话",
                        model = new { id = "fake-model", provider = "fake" },
                    },
                },
                "get_messages" => new
                {
                    id,
                    type = "response",
                    command,
                    success = true,
                    data = new
                    {
                        messages = new object[]
                        {
                            new { role = "user", content = "历史问题" },
                            new
                            {
                                role = "assistant",
                                content = new object[] { new { type = "text", text = "历史回答" } },
                                stopReason = "stop",
                            },
                        },
                    },
                },
                "clear_queue" => new { id, type = "response", command, success = true, data = new { steering = Array.Empty<string>(), followUp = Array.Empty<string>() } },
                _ => new { id, type = "response", command, success = true, data = new { } },
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(response));

            if (command == "prompt")
            {
                var promptMessage = root.TryGetProperty("message", out var promptElement)
                    ? promptElement.GetString()
                    : null;
                if (promptMessage == "触发模型错误")
                {
                    WriteFakeEvent(new { type = "agent_start" });
                    WriteFakeEvent(new { type = "message_start", message = new { role = "assistant", content = Array.Empty<object>() } });
                    WriteFakeEvent(new
                    {
                        type = "message_end",
                        message = new
                        {
                            role = "assistant",
                            content = Array.Empty<object>(),
                            stopReason = "error",
                            errorMessage = "429：套餐已过期",
                        },
                    });
                    WriteFakeEvent(new { type = "agent_settled" });
                    Console.Out.Flush();
                    continue;
                }

                WriteFakeEvent(new { type = "agent_start" });
                WriteFakeEvent(new { type = "message_start", message = new { role = "assistant", content = Array.Empty<object>() } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "thinking_start", contentIndex = 0 } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "thinking_delta", contentIndex = 0, delta = "思考过程" } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "text_start", contentIndex = 1 } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "text_delta", contentIndex = 1, delta = "流式" } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "text_delta", contentIndex = 1, delta = "回复" } });
                WriteFakeEvent(new { type = "message_update", assistantMessageEvent = new { type = "toolcall_start", contentIndex = 2, id = "tool-1", toolName = "read" } });
                WriteFakeEvent(new { type = "tool_execution_start", toolCallId = "tool-1", toolName = "read", args = new { path = "demo.txt" } });
                WriteFakeEvent(new
                {
                    type = "tool_execution_end",
                    toolCallId = "tool-1",
                    toolName = "read",
                    result = new { content = new object[] { new { type = "text", text = "工具结果" } } },
                    isError = false,
                });
                WriteFakeEvent(new
                {
                    type = "message_end",
                    message = new
                    {
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "thinking", thinking = "思考过程" },
                            new { type = "text", text = "流式回复" },
                            new { type = "toolCall", id = "tool-1", name = "read", arguments = new { path = "demo.txt" } },
                        },
                        usage = new
                        {
                            input = 230,
                            output = 70,
                            cacheRead = 50,
                            cacheWrite = 0,
                            reasoning = 15,
                            totalTokens = 350,
                        },
                        stopReason = "stop",
                    },
                });
                WriteFakeEvent(new { type = "agent_settled" });
            }

            if (command == "abort")
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    type = "test_command_order",
                    commands = receivedCommands.ToArray(),
                }));
            }

            Console.Out.Flush();
        }

        return 0;
    }

    private static async Task RunLiveChatProbeAsync(string projectDirectory, string modelName)
    {
        var projectPath = Path.GetFullPath(projectDirectory);
        Directory.CreateDirectory(projectPath);
        var sessionDirectory = Path.Combine(projectPath, ".test-sessions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);

        string sessionFile;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var assistantText = new StringBuilder();
        try
        {
            await using (var client = new PiRpcClient())
            {
                client.EventReceived += (_, rpcEvent) =>
                {
                    if (rpcEvent.Type == "agent_settled")
                    {
                        settled.TrySetResult();
                    }
                    else if (rpcEvent.Type == "message_update" &&
                             rpcEvent.Payload.TryGetProperty("assistantMessageEvent", out var messageEvent) &&
                             messageEvent.TryGetProperty("type", out var type) && type.GetString() == "text_delta" &&
                             messageEvent.TryGetProperty("delta", out var delta))
                    {
                        lock (assistantText)
                        {
                            assistantText.Append(delta.GetString());
                        }
                    }
                };
                await client.StartAsync(
                    new PiStartOptions(projectPath, SessionDirectory: sessionDirectory),
                    CancellationToken.None).ConfigureAwait(false);
                var modelParts = modelName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (modelParts.Length != 2)
                {
                    throw new InvalidOperationException($"实机模型必须使用 provider/model 格式：{modelName}");
                }

                await client.RequestAsync(
                    "set_model",
                    new Dictionary<string, object?> { ["provider"] = modelParts[0], ["modelId"] = modelParts[1] },
                    TimeSpan.FromSeconds(20),
                    CancellationToken.None).ConfigureAwait(false);
                await client.SendPromptAsync("仅回复 PI_HARNESS_TEST_OK，不要调用工具。", CancellationToken.None).ConfigureAwait(false);
                await settled.Task.WaitAsync(TimeSpan.FromMinutes(3)).ConfigureAwait(false);

                var state = await client.RequestAsync("get_state", CancellationToken.None).ConfigureAwait(false);
                sessionFile = state.GetProperty("data").GetProperty("sessionFile").GetString()
                    ?? throw new InvalidOperationException("pi 未返回会话文件路径");
                var lastTextResponse = await client.RequestAsync("get_last_assistant_text", CancellationToken.None).ConfigureAwait(false);
                var messagesResponse = await client.RequestAsync("get_messages", CancellationToken.None).ConfigureAwait(false);
                var lastTextData = lastTextResponse.GetProperty("data");
                var finalAssistantText = lastTextData.TryGetProperty("text", out var textElement)
                    ? textElement.GetString() ?? string.Empty
                    : string.Empty;
                AssertLive(File.Exists(sessionFile), "新会话未持久化");
                if (!finalAssistantText.Contains("PI_HARNESS_TEST_OK", StringComparison.Ordinal))
                {
                    var messages = messagesResponse.GetProperty("data").GetProperty("messages");
                    var errorMessage = messages.EnumerateArray()
                        .Where(message => message.TryGetProperty("role", out var role) && role.GetString() == "assistant")
                        .Select(message => message.TryGetProperty("errorMessage", out var error) ? error.GetString() : null)
                        .LastOrDefault(error => !string.IsNullOrWhiteSpace(error));
                    throw new InvalidOperationException(errorMessage is null
                        ? $"最终回复不符合预期：{finalAssistantText}"
                        : $"模型 {modelName} 返回错误：{errorMessage}");
                }
                Console.WriteLine($"[实机信息] 增量文本 {assistantText.Length} 字符，最终文本 {finalAssistantText.Length} 字符");
            }

            await using (var resumedClient = new PiRpcClient())
            {
                await resumedClient.StartAsync(
                    new PiStartOptions(projectPath, SessionPath: sessionFile, SessionDirectory: sessionDirectory),
                    CancellationToken.None).ConfigureAwait(false);
                var response = await resumedClient.RequestAsync("get_messages", CancellationToken.None).ConfigureAwait(false);
                var messages = response.GetProperty("data").GetProperty("messages");
                AssertLive(messages.GetArrayLength() >= 2, "重新打开后历史消息不完整");
            }

            Console.WriteLine($"[实机通过] TEST-07/08：新建、流式回复、持久化和继续会话成功，文件 {Path.GetFileName(sessionFile)}");
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }

    private static void AssertLive(bool bCondition, string message)
    {
        if (!bCondition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void WriteFakeEvent(object value)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(value));
    }

    private static string? ReadOption(IReadOnlyList<string> args, string option)
    {
        for (var nIndex = 0; nIndex + 1 < args.Count; nIndex++)
        {
            if (string.Equals(args[nIndex], option, StringComparison.Ordinal))
            {
                return args[nIndex + 1];
            }
        }

        return null;
    }
}

internal static class TestContext
{
    public static IReadOnlyList<string> Arguments { get; set; } = [];
}
