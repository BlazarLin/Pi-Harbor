using System.IO;

namespace PIHarness.App.Presentation;

public sealed record ComposerSuggestion(string Label, string Description, string InsertText, bool IsFile = false);
public sealed record CompletionQuery(int Start, int Length, char Trigger, string Filter);

public static class ComposerCompletion
{
    public const int MaxFileResults = 120;
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cpp", ".h", ".hpp", ".c", ".cc", ".cxx", ".hxx", ".cs", ".py", ".ts", ".tsx", ".js", ".jsx", ".rs", ".go", ".java", ".xaml", ".txt", ".md", ".json", ".yaml", ".yml", ".xml", ".toml", ".ps1", ".cmake" };
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", "node_modules", "bin", "obj", "artifacts", "dist", "build", ".venv", "__pycache__" };

    public static CompletionQuery? GetQuery(string text, int caret)
    {
        if (caret < 0 || caret > text.Length) return null;
        if (text.StartsWith("/file ", StringComparison.OrdinalIgnoreCase) && caret >= 6)
            return new CompletionQuery(0, caret, '@', text[6..caret]);
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
        while (directories.Count > 0 && visited < 30000 && timer.ElapsedMilliseconds < 1500)
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
                    matches.Add(new ComposerSuggestion(path, "项目文件 · 插入紧凑引用", $"\"{path}\" ", IsFile: true));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return matches.OrderBy(item => SourceExtensions.Contains(Path.GetExtension(item.Label)) ? 0 : 1)
            .ThenBy(item => Path.GetFileName(item.Label).StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase).Take(MaxFileResults).ToArray();
    }
}
