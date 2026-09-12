// Created: 2026-09-08
// Purpose: Persist user starred ("重点关注") sessions separately from Pi's append-only conversation files.

using System.Text;
using System.Text.Json;

namespace PIHarness.Core.Sessions;

public sealed class SessionFlagStore(string directory)
{
    private readonly string _filePath = Path.Combine(Path.GetFullPath(directory), "starred-sessions.json");

    public async Task<HashSet<string>> LoadAsync(CancellationToken token = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(_filePath, token).ConfigureAwait(false);
            var paths = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        catch (DirectoryNotFoundException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public async Task SaveAsync(IReadOnlyCollection<string> starredPaths, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(starredPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), token).ConfigureAwait(false);
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
