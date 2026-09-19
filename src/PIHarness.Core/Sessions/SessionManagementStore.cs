using System.Text.Json;

namespace PIHarness.Core.Sessions;

public sealed record SessionManagementState(string[] Archived, string[] Unread, string? DefaultWorkingDirectory)
{
    public static SessionManagementState Empty => new([], [], null);
}

/// <summary>Local UI metadata; never rewrites Pi's conversation files.</summary>
public sealed class SessionManagementStore(string directory)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "session-management.json");

    public async Task<SessionManagementState> LoadAsync()
    {
        try
        {
            var state = JsonSerializer.Deserialize<SessionManagementState>(await File.ReadAllTextAsync(_path).ConfigureAwait(false));
            return state is { Archived: not null, Unread: not null } ? state : SessionManagementState.Empty;
        }
        catch (FileNotFoundException) { return SessionManagementState.Empty; }
        catch (DirectoryNotFoundException) { return SessionManagementState.Empty; }
        catch (JsonException) { return SessionManagementState.Empty; }
    }

    public async Task SaveAsync(SessionManagementState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state)).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
