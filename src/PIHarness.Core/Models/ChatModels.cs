// Created: 2026-09-06
// Purpose: Define presentation-neutral categories for pi conversation content.

namespace PIHarness.Core.Models;

public enum ChatItemKind
{
    User,
    Assistant,
    Thinking,
    Tool,
    Metrics,
    System,
    Error,
}

public sealed record SessionHistoryItem(
    ChatItemKind Kind,
    string Text,
    string Title = "",
    string Key = "",
    bool IsError = false,
    TurnMetrics? Metrics = null);

public readonly record struct TokenUsage(
    long Input,
    long Output,
    long CacheRead,
    long CacheWrite,
    long Reasoning,
    long TotalTokens)
{
    public static TokenUsage operator +(TokenUsage left, TokenUsage right) => new(
        left.Input + right.Input,
        left.Output + right.Output,
        left.CacheRead + right.CacheRead,
        left.CacheWrite + right.CacheWrite,
        left.Reasoning + right.Reasoning,
        left.TotalTokens + right.TotalTokens);
}

public sealed record TurnMetrics(TokenUsage Usage, TimeSpan? Duration)
{
    public long Input => Usage.Input;
    public long Output => Usage.Output;
    public long CacheRead => Usage.CacheRead;
    public long CacheWrite => Usage.CacheWrite;
    public long Reasoning => Usage.Reasoning;
    public long TotalTokens => Usage.TotalTokens;

    public string ToDisplayText()
    {
        var duration = Duration.HasValue ? FormatDuration(Duration.Value) : "耗时未知";
        return $"⏱ {duration}  ·  Tokens {TotalTokens:N0}（输入 {Input:N0} / 输出 {Output:N0} / 缓存 {CacheRead:N0}）";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
        {
            return $"{Math.Max(0, duration.TotalMilliseconds):F0} ms";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{duration.TotalSeconds:F1} 秒";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes} 分 {duration.Seconds} 秒";
        }

        return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒";
    }
}

public sealed record SessionHistorySnapshot(
    IReadOnlyList<SessionHistoryItem> Items,
    IReadOnlyList<string> Warnings,
    int EntryCount);
