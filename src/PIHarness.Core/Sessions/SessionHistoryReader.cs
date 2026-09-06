// Created: 2026-09-06
// Purpose: Read the active pi session branch without retaining image payloads in the UI model.

using System.Text;
using System.Text.Json;
using PIHarness.Core.Models;

namespace PIHarness.Core.Sessions;

public static class SessionHistoryReader
{
    private const int MaxWarnings = 8;
    private const int MaxEntries = 200_000;

    public static async Task<SessionHistorySnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var nodes = new Dictionary<string, HistoryNode>(StringComparer.Ordinal);
        var warnings = new List<string>();
        string? leafId = null;
        var nLine = 0;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 128 * 1024,
            leaveOpen: false);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            nLine++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = ReadString(root, "id");
                if (string.IsNullOrWhiteSpace(id) || ReadString(root, "type") == "session")
                {
                    continue;
                }

                if (nodes.Count >= MaxEntries && !nodes.ContainsKey(id))
                {
                    throw new InvalidDataException($"会话条目超过安全上限 {MaxEntries}。");
                }

                var parentId = ReadNullableString(root, "parentId");
                nodes[id] = new HistoryNode(id, parentId, MapEntry(root));
                leafId = id;
            }
            catch (JsonException exception)
            {
                AddWarning(warnings, $"{path} 第 {nLine} 行 JSON 损坏：{exception.Message}");
            }
        }

        var branch = new List<HistoryNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(leafId) && nodes.TryGetValue(leafId, out var node))
        {
            if (!visited.Add(node.Id))
            {
                AddWarning(warnings, $"{path}：检测到 parentId 循环，已停止回溯。");
                break;
            }

            branch.Add(node);
            leafId = node.ParentId;
        }

        branch.Reverse();
        var items = new List<SessionHistoryItem>(branch.Sum(node => node.Items.Count));
        foreach (var node in branch)
        {
            items.AddRange(node.Items);
        }

        return new SessionHistorySnapshot(items, warnings, nodes.Count);
    }

    private static IReadOnlyList<SessionHistoryItem> MapEntry(JsonElement entry)
    {
        var type = ReadString(entry, "type");
        if (type == "message" && entry.TryGetProperty("message", out var message))
        {
            return MapMessage(message);
        }

        if (type is "compaction" or "branch_summary")
        {
            var summary = ReadString(entry, "summary");
            return string.IsNullOrWhiteSpace(summary)
                ? []
                : [new SessionHistoryItem(ChatItemKind.System, summary)];
        }

        if (type == "custom_message" && !IsFalse(entry, "display"))
        {
            var text = ReadContentText(entry);
            return string.IsNullOrWhiteSpace(text)
                ? []
                : [new SessionHistoryItem(ChatItemKind.System, text)];
        }

        return [];
    }

    private static IReadOnlyList<SessionHistoryItem> MapMessage(JsonElement message)
    {
        var result = new List<SessionHistoryItem>();
        var role = ReadString(message, "role");
        switch (role)
        {
            case "user":
                AddIfNotEmpty(result, ChatItemKind.User, ReadContentText(message));
                break;
            case "assistant":
                if (message.TryGetProperty("content", out var assistantContent) && assistantContent.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in assistantContent.EnumerateArray())
                    {
                        var type = ReadString(part, "type");
                        if (type == "text")
                        {
                            AddIfNotEmpty(result, ChatItemKind.Assistant, ReadString(part, "text"));
                        }
                        else if (type == "thinking")
                        {
                            AddIfNotEmpty(result, ChatItemKind.Thinking, ReadString(part, "thinking"), "思考过程");
                        }
                        else if (type == "toolCall")
                        {
                            var arguments = part.TryGetProperty("arguments", out var args) ? args.GetRawText() : string.Empty;
                            result.Add(new SessionHistoryItem(
                                ChatItemKind.Tool,
                                arguments,
                                $"工具：{ReadString(part, "name")}",
                                ReadString(part, "id")));
                        }
                    }
                }

                if (ReadString(message, "stopReason") == "error")
                {
                    AddIfNotEmpty(result, ChatItemKind.Error, ReadString(message, "errorMessage"));
                }
                break;
            case "toolResult":
                result.Add(new SessionHistoryItem(
                    ChatItemKind.Tool,
                    ReadContentText(message),
                    $"工具结果：{ReadString(message, "toolName")}",
                    ReadString(message, "toolCallId"),
                    IsTrue(message, "isError")));
                break;
            case "bashExecution":
                AddIfNotEmpty(
                    result,
                    ChatItemKind.Tool,
                    ReadString(message, "output"),
                    $"命令：{ReadString(message, "command")}");
                break;
            case "custom":
                if (!IsFalse(message, "display"))
                {
                    AddIfNotEmpty(result, ChatItemKind.System, ReadContentText(message));
                }
                break;
            case "branchSummary":
            case "compactionSummary":
                AddIfNotEmpty(result, ChatItemKind.System, ReadString(message, "summary"));
                break;
        }

        return result;
    }

    private static string ReadContentText(JsonElement container)
    {
        if (!container.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return content.ToString();
        }

        var textParts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            if (ReadString(part, "type") != "text")
            {
                continue;
            }

            var text = ReadString(part, "text");
            if (!string.IsNullOrEmpty(text))
            {
                textParts.Add(text);
            }
        }

        return string.Join(Environment.NewLine, textParts);
    }

    private static void AddIfNotEmpty(
        ICollection<SessionHistoryItem> target,
        ChatItemKind kind,
        string text,
        string title = "")
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            target.Add(new SessionHistoryItem(kind, text, title));
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static bool IsTrue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static bool IsFalse(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.False;

    private static void AddWarning(ICollection<string> warnings, string warning)
    {
        if (warnings.Count < MaxWarnings)
        {
            warnings.Add(warning);
        }
    }

    private sealed record HistoryNode(string Id, string? ParentId, IReadOnlyList<SessionHistoryItem> Items);
}
