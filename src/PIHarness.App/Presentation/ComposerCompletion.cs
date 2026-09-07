using System.IO;

namespace PIHarness.App.Presentation;

public sealed record ComposerSuggestion(string Label, string Description, string InsertText);
public sealed record CompletionQuery(int Start, int Length, char Trigger, string Filter);

public static class ComposerCompletion
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", "node_modules", "bin", "obj", "artifacts", "dist", "build", ".venv", "__pycache__" };

    public static CompletionQuery? GetQuery(string text, int caret)
    {
        if (caret < 0 || caret > text.Length) return null;
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        if (start == caret || text[start] is not ('@' or '/')) return null;
        // Pi expands slash commands only at the beginning of the prompt.
        if (text[start] == '/' && !string.IsNullOrWhiteSpace(text[..start])) return null;
        return new CompletionQuery(start, caret - start, text[start], text[(start + 1)..caret]);
    }

    public static IReadOnlyList<ComposerSuggestion> FindFiles(string cwd, string filter, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cwd)) return [];
        var matches = new List<ComposerSuggestion>();
        var directories = new Queue<string>();
        directories.Enqueue(cwd);
        var visited = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (directories.Count > 0 && visited < 30000 && matches.Count < 40 && timer.ElapsedMilliseconds < 1500)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Dequeue();
            try
            {
                foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > 30000 || timer.ElapsedMilliseconds >= 1500) break;
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!ExcludedDirectories.Contains(entry.Name)) directories.Enqueue(entry.FullName);
                        continue;
                    }
                    var path = Path.GetRelativePath(cwd, entry.FullName).Replace('\\', '/');
                    if (!path.Contains(filter.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;
                    // RPC does not expand CLI @file arguments; insert an explicit path reference for the agent to read.
                    matches.Add(new ComposerSuggestion(path, "项目文件 · 插入路径引用", $"\"{path}\" "));
                    if (matches.Count >= 40) break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return matches.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
