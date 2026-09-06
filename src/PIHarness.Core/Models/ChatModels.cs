// Created: 2026-09-06
// Purpose: Define presentation-neutral categories for pi conversation content.

namespace PIHarness.Core.Models;

public enum ChatItemKind
{
    User,
    Assistant,
    Thinking,
    Tool,
    System,
    Error,
}

public sealed record SessionHistoryItem(
    ChatItemKind Kind,
    string Text,
    string Title = "",
    string Key = "",
    bool IsError = false);

public sealed record SessionHistorySnapshot(
    IReadOnlyList<SessionHistoryItem> Items,
    IReadOnlyList<string> Warnings,
    int EntryCount);
