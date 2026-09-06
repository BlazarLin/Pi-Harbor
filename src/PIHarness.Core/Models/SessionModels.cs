// Created: 2026-09-06
// Purpose: Define bounded session index data shared by discovery and UI layers.

namespace PIHarness.Core.Models;

public sealed record SessionSummary(
    string SessionPath,
    string Cwd,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);

public sealed record ProjectGroup(
    string Cwd,
    string DisplayName,
    IReadOnlyList<SessionSummary> Sessions,
    DateTimeOffset LastActivityAt);

public sealed record SessionParseResult(
    bool IsValid,
    SessionSummary? Session,
    IReadOnlyList<string> Warnings);

public sealed record CatalogSnapshot(
    IReadOnlyList<ProjectGroup> Projects,
    int SessionCount,
    int SkippedFileCount,
    IReadOnlyList<string> Warnings);
