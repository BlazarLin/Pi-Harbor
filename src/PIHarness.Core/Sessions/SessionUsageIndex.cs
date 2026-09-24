using System.Globalization;
using System.Text.Json;

namespace PIHarness.Core.Sessions;

public sealed record DailyUsage(DateOnly Date, long Tokens, int Replies);
public sealed record UsageOverview(IReadOnlyList<DailyUsage> Days, int UnreadableFiles, int InvalidRecords);

/// <summary>Aggregate usage from every locally discovered session, including archived and terminal sessions.</summary>
public sealed class SessionUsageIndex
{
    private sealed record Cached(long Length, DateTime LastWrite, DateOnly Start, DailyUsage[] Days, int Invalid);
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<UsageOverview> ReadAsync(IEnumerable<string> paths, DateOnly today, CancellationToken token)
    {
        var start = today.AddDays(-27);
        var totals = Enumerable.Range(0, 28).ToDictionary(i => start.AddDays(i), i => new DailyUsage(start.AddDays(i), 0, 0));
        var unreadable = 0;
        var invalid = 0;
        var currentPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in currentPaths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!_cache.TryGetValue(path, out var cached) || cached.Length != info.Length || cached.LastWrite != info.LastWriteTimeUtc || cached.Start != start)
                {
                    var length = info.Length;
                    var stamp = info.LastWriteTimeUtc;
                    var days = new Dictionary<DateOnly, DailyUsage>();
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    var malformed = 0;
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var reader = new StreamReader(stream);
                    while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var document = JsonDocument.Parse(line);
                            var entry = document.RootElement;
                            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                                !message.TryGetProperty("role", out var role) || role.GetString() != "assistant" ||
                                !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) continue;
                            if (entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !ids.Add(id.GetString()!)) continue;
                            DateTimeOffset time;
                            if (entry.TryGetProperty("timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String &&
                                DateTimeOffset.TryParse(timestamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time)) { }
                            else if (message.TryGetProperty("timestamp", out timestamp) && timestamp.TryGetInt64(out var millis)) time = DateTimeOffset.FromUnixTimeMilliseconds(millis);
                            else { malformed++; continue; }
                            var day = DateOnly.FromDateTime(time.LocalDateTime);
                            if (day < start || day > today) continue;
                            var tokens = Number(usage, "totalTokens");
                            if (tokens == 0) tokens = Number(usage, "input") + Number(usage, "output") + Number(usage, "cacheRead") + Number(usage, "cacheWrite");
                            var previous = days.GetValueOrDefault(day, new DailyUsage(day, 0, 0));
                            days[day] = previous with { Tokens = checked(previous.Tokens + tokens), Replies = previous.Replies + 1 };
                        }
                        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentOutOfRangeException or OverflowException) { malformed++; }
                    }
                    cached = new Cached(length, stamp, start, days.Values.ToArray(), malformed);
                    _cache[path] = cached;
                }
                invalid += cached.Invalid;
                foreach (var day in cached.Days)
                    totals[day.Date] = totals[day.Date] with { Tokens = totals[day.Date].Tokens + day.Tokens, Replies = totals[day.Date].Replies + day.Replies };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { unreadable++; }
        }
        var active = currentPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _cache.Keys.Where(path => !active.Contains(path)).ToArray()) _cache.Remove(removed);
        return new UsageOverview(totals.Values.OrderBy(day => day.Date).ToArray(), unreadable, invalid);
    }

    private static long Number(JsonElement value, string name) => value.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var result) ? Math.Max(0, result) : 0;
}
