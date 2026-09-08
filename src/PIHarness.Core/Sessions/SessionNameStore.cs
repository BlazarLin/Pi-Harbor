using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PIHarness.Core.Sessions;

/// <summary>Local display names, separate from Pi's append-only conversation files.</summary>
public sealed class SessionNameStore(string directory)
{
    public const int MaxNameLength = 120;
    private readonly string _directory = Path.GetFullPath(directory);

    public async Task<string?> ReadAsync(string sessionPath, CancellationToken token)
    {
        var path = NamePath(sessionPath);
        try
        {
            var value = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
            return Validate(value);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task SaveAsync(string sessionPath, string? name, CancellationToken token = default)
    {
        var path = NamePath(sessionPath);
        if (name is null)
        {
            try { File.Delete(path); } catch (DirectoryNotFoundException) { }
            return;
        }
        name = Validate(name);
        Directory.CreateDirectory(_directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(name), new UTF8Encoding(false), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Validate(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("请输入会话名称。");
        if (value.Length > MaxNameLength) throw new ArgumentException($"名称最多 {MaxNameLength} 个字符。");
        if (value.Any(char.IsControl)) throw new ArgumentException("名称不能包含换行或控制字符。");
        return value;
    }

    private string NamePath(string sessionPath)
    {
        var key = Path.GetFullPath(sessionPath).ToUpperInvariant();
        return Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    }
}
