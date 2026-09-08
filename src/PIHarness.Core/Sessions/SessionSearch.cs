using System.Text;
using System.Text.Json;
using PIHarness.Core.Models;

namespace PIHarness.Core.Sessions;

public sealed record SessionSearchMatch(SessionSummary Session, string Preview, string Source);
public sealed record SessionSearchResult(IReadOnlyList<SessionSearchMatch> Matches, bool Truncated, int UnreadableFiles, int InvalidRecords);

public static class SessionSearch
{
    public const int MaxResults = 100;

    // Search every saved branch, one JSONL record at a time. Only snippets are retained.
    public static async Task<SessionSearchResult> FindAsync(IEnumerable<SessionSummary> sessions, string query, CancellationToken token)
    {
        query = query.Trim();
        if (query.Length == 0) return new([], false, 0, 0);
        var results = new List<SessionSearchMatch>();
        var unreadable = 0;
        var invalid = 0;
        foreach (var session in sessions.OrderByDescending(item => item.LastActivityAt))
        {
            token.ThrowIfCancellationRequested();
            var preview = Snippet(session.Title, query);
            var source = "名称";
            if (preview is null) { preview = Snippet(session.Cwd, query); source = "项目"; }
            if (preview is null)
            {
                try
                {
                    await using var stream = new FileStream(session.SessionPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
                    while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                    {
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.ValueKind != JsonValueKind.Object) { invalid++; continue; }
                            if (String(root, "type") != "message" || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) continue;
                            if (!message.TryGetProperty("content", out var content)) continue;
                            foreach (var (text, kind) in TextParts(content))
                            {
                                token.ThrowIfCancellationRequested();
                                preview = Snippet(text, query);
                                if (preview is null) continue;
                                source = kind ?? (String(message, "role") switch { "user" => "用户", "assistant" => "助手", "toolResult" => "工具输出", _ => "消息" });
                                break;
                            }
                            if (preview is not null) break;
                        }
                        catch (JsonException) { invalid++; }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException) { unreadable++; }
            }
            if (preview is null) continue;
            if (results.Count == MaxResults) return new(results, true, unreadable, invalid);
            results.Add(new(session, preview, source));
        }
        return new(results, false, unreadable, invalid);
    }

    private static IEnumerable<(string Text, string? Kind)> TextParts(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) { yield return (content.GetString()!, null); yield break; }
        if (content.ValueKind != JsonValueKind.Array) yield break;
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;
            if (String(part, "type") == "text" && String(part, "text") is { } text) yield return (text, null);
            if (String(part, "type") == "thinking" && String(part, "thinking") is { } thinking) yield return (thinking, "思考");
            // Image payloads, signatures, IDs and other protocol fields are deliberately not searchable.
        }
    }

    private static string? String(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static string? Snippet(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        var start = Math.Max(0, index - 36);
        if (start > 0 && char.IsLowSurrogate(text[start])) start--;
        var end = Math.Min(text.Length, Math.Max(index + Math.Min(query.Length, 160), start + 160));
        if (end < text.Length && char.IsLowSurrogate(text[end])) end++;
        return (start > 0 ? "…" : "") + text[start..end].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ') + (end < text.Length ? "…" : "");
    }
}
