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
