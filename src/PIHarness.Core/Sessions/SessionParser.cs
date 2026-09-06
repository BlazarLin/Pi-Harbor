// Created: 2026-09-06
// Purpose: Build a compact, fault-tolerant index entry from one pi JSONL session.

using System.Globalization;
using System.Text;
using System.Text.Json;
using PIHarness.Core.Models;

namespace PIHarness.Core.Sessions;

public static class SessionParser
{
    private const int MaxWarningsPerFile = 8;
    private const int MaxTitleLength = 512;
    private const long FullScanThresholdBytes = 1024 * 1024;
    private const int TailScanBytes = 512 * 1024;
    private const int MaxHeadLines = 256;

    public static async Task<SessionParseResult> ParseAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var warnings = new List<string>();
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                useAsync: true);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);

            var firstLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return Invalid(path, "会话文件为空");
            }

            if (!TryParseDocument(firstLine, out var headerDocument) || headerDocument is null)
            {
                return Invalid(path, "会话头不是有效 JSON");
            }

            string? cwd;
            DateTimeOffset createdAt;
            using (headerDocument)
            {
                var root = headerDocument.RootElement;
                if (!TryGetString(root, "type", out var type) || type != "session" ||
                    !TryGetString(root, "cwd", out cwd) || string.IsNullOrWhiteSpace(cwd))
                {
                    return Invalid(path, "会话头缺少 type=session 或 cwd");
                }

                createdAt = ReadTimestamp(root) ?? new DateTimeOffset(File.GetCreationTimeUtc(path));
            }

            string? firstUserText = null;
            string? sessionName = null;
            var lastActivityAt = createdAt;

            if (stream.Length <= FullScanThresholdBytes)
            {
                var nLine = 1;
                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    nLine++;
                    ProcessIndexLine(path, line, nLine, warnings, ref firstUserText, ref sessionName, ref lastActivityAt);
                }
            }
            else
            {
                lastActivityAt = Max(lastActivityAt, new DateTimeOffset(File.GetLastWriteTimeUtc(path)));
                firstUserText = await ReadMetaFirstMessageAsync(path, cancellationToken).ConfigureAwait(false);
                if (firstUserText is null)
                {
                    for (var nLine = 2; nLine <= MaxHeadLines + 1; nLine++)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (line is null)
                        {
                            break;
                        }

                        ProcessIndexLine(path, line, nLine, warnings, ref firstUserText, ref sessionName, ref lastActivityAt);
                        if (firstUserText is not null)
                        {
                            break;
                        }
                    }
                }

                sessionName = await ReadTailSessionNameAsync(path, sessionName, cancellationToken).ConfigureAwait(false);
            }

            var title = sessionName ?? firstUserText ?? createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var summary = new SessionSummary(
                System.IO.Path.GetFullPath(path),
                cwd!,
                title,
                createdAt,
                lastActivityAt);
            return new SessionParseResult(true, summary, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return Invalid(path, $"读取会话失败：{exception.Message}");
        }
    }

    private static void ProcessIndexLine(
        string path,
        string line,
        int nLine,
        List<string> warnings,
        ref string? firstUserText,
        ref string? sessionName,
        ref DateTimeOffset lastActivityAt)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (!TryParseDocument(line, out var document) || document is null)
        {
            AddWarning(warnings, $"{path} 第 {nLine} 行不是有效 JSON");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var timestamp = ReadTimestamp(root);
            if (timestamp.HasValue && timestamp.Value > lastActivityAt)
            {
                lastActivityAt = timestamp.Value;
            }

            if (!TryGetString(root, "type", out var entryType))
            {
                return;
            }

            if (entryType == "session_info" && TryGetString(root, "name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                sessionName = NormalizeTitle(name);
            }
            else if (entryType == "message" && firstUserText is null)
            {
                firstUserText = ReadFirstUserText(root);
            }
        }
    }

    private static async Task<string?> ReadMetaFirstMessageAsync(string sessionPath, CancellationToken cancellationToken)
    {
        var metaPath = System.IO.Path.ChangeExtension(sessionPath, ".meta.json");
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                metaPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4 * 1024,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return TryGetString(document.RootElement, "firstMessage", out var firstMessage)
                ? NormalizeTitle(firstMessage)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadTailSessionNameAsync(
        string path,
        string? currentName,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            useAsync: true);
        var start = Math.Max(0, stream.Length - TailScanBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: start == 0);
        if (start > 0)
        {
            await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.Contains("session_info", StringComparison.Ordinal) ||
                !TryParseDocument(line, out var document) || document is null)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (TryGetString(root, "type", out var type) && type == "session_info" &&
                    TryGetString(root, "name", out var name) && !string.IsNullOrWhiteSpace(name))
                {
                    currentName = NormalizeTitle(name);
                }
            }
        }

        return currentName;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static string? ReadFirstUserText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !TryGetString(message, "role", out var role) || role != "user" ||
            !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return NormalizeTitle(content.GetString());
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object &&
                TryGetString(part, "type", out var partType) && partType == "text" &&
                TryGetString(part, "text", out var text))
            {
                var normalized = NormalizeTitle(text);
                if (!string.IsNullOrEmpty(normalized))
                {
                    return normalized;
                }
            }
        }

        return null;
    }

    private static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxTitleLength ? normalized : normalized[..MaxTitleLength];
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element)
    {
        return TryGetString(element, "timestamp", out var timestamp) &&
               DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryParseDocument(string line, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(line);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < MaxWarningsPerFile)
        {
            warnings.Add(warning);
        }
    }

    private static SessionParseResult Invalid(string path, string reason) =>
        new(false, null, [$"{path}：{reason}"]);
}
