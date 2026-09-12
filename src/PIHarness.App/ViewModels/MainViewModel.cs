// Created: 2026-09-06
// Purpose: Coordinate the local session catalog, per-conversation pi RPC processes, starred sessions,
//          and chat presentation state. Each conversation runs independently so several sessions can
//          stream in parallel; the sidebar tree keeps operating while a turn is in flight.

using System.Collections.ObjectModel;
using System.IO;
using PIHarness.App.Presentation;
using PIHarness.Core.Models;
using PIHarness.Core.Rpc;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public enum ChatSessionState
{
    Idle,
    Starting,
    Ready,
    SwitchingModel,
    Streaming,
    Stopping,
    Faulted,
}

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    public const string ProductName = "Pi Harbor";
    public const string ProductSubtitle = "Pi Session Desk";
    public static string ApplicationVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.5.0";
    private readonly string _sessionRoot;
    private readonly Func<PiRpcClient> _rpcClientFactory;
    private readonly SessionCatalog _catalog;
    private readonly SessionFlagStore _flagStore;
    private readonly HashSet<string> _starredSessions;
    private readonly HashSet<string> _customRenames = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _flagLock = new(1, 1);
    private readonly SemaphoreSlim _catalogRefreshLock = new(1, 1);
    private int _catalogRefreshRequested;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<string, ConversationViewModel> _conversations = new(StringComparer.OrdinalIgnoreCase);
    private bool _shuttingDown;
    private bool _disposed;

    public MainViewModel(string? sessionRoot = null, Func<PiRpcClient>? rpcClientFactory = null, string? namesDirectory = null)
    {
        _sessionRoot = Path.GetFullPath(sessionRoot ?? GetDefaultSessionRoot());
        _rpcClientFactory = rpcClientFactory ?? (() => new PiRpcClient());
        var appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pi Harbor");
        _nameStore = new SessionNameStore(namesDirectory ?? Path.Combine(appDataDirectory, "session-names"));
        _flagStore = new SessionFlagStore(namesDirectory is null
            ? Path.Combine(appDataDirectory, "session-flags")
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(namesDirectory))!, "session-flags"));
        _starredSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _catalog = new SessionCatalog(_sessionRoot, _nameStore);
        _catalog.Changed += OnCatalogChanged;
        _uiContext = SynchronizationContext.Current;

        RefreshCommand = new AsyncRelayCommand(RefreshCatalogAsync, () => true);
        Active = CreateConversation("initial", "选择一个会话");
        _ = LoadStarredSessionsAsync();
    }

    public ConversationViewModel Active { get; private set; }

    /// <summary>Raised when a background conversation settles a turn or its pi process fails.</summary>
    public event Action<ConversationViewModel, string>? ConversationAttention;

    public ObservableCollection<ProjectGroupViewModel> Projects { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SendCommand => Active.SendCommand;
    public AsyncRelayCommand StopCommand => Active.StopCommand;
    public AsyncRelayCommand ReconnectCommand => Active.ReconnectCommand;
    public string AppVersionText => $"v{ApplicationVersion}";
    public string WindowTitle => $"{ProductName} {ApplicationVersion} — {ProductSubtitle}";

    // Forwarded conversation state; the active conversation can change at any time.
    public BulkObservableCollection<ChatItemViewModel> Messages => Active.Messages;
    public ObservableCollection<ImageAttachmentViewModel> Attachments => Active.Attachments;
    public ObservableCollection<ComposerSuggestion> SlashCommands => Active.SlashCommands;
    public ObservableCollection<ModelOptionViewModel> Models => Active.Models;
    public string CommandLoadStatus => Active.CommandLoadStatus;
    public string InputText
    {
        get => Active.InputText;
        set => Active.InputText = value;
    }
    public string CurrentTitle => Active.Title;
    public string CurrentCwd => Active.Cwd;
    public string ModelText => Active.ModelText;
    public ModelOptionViewModel? SelectedModel => Active.SelectedModel;
    public string StatusText => Active.StatusText;
    public bool CanEditComposer => Active.CanEditComposer;
    public string? SelectedSessionPath => Active.SessionPath;
    public ChatSessionState State => Active.State;
    public bool CanSend => Active.CanSend;
    public bool CanSwitchSession => Active.CanSwitchSession;
    public bool CanChangeModel => Active.CanChangeModel;
    public bool IsStreaming => Active.IsStreaming;
    public bool CanReconnect => Active.CanReconnect;
    public string EmptyConversationText => Active.EmptyConversationText;
    public string MessageCountText => Active.MessageCountText;
    public string TotalTokensText => Active.TotalTokensText;
    public bool IsLoadingHistory => Active.IsLoadingHistory;

    /// <summary>Number of open conversations that finished (or failed) without being viewed.</summary>
    public int UnreadCount => _conversations.Values.Count(conversation => conversation.HasUnread);
    public int BusyCount => _conversations.Values.Count(conversation => conversation.IsBusy);
    public string BackgroundActivityText
    {
        get
        {
            var parts = new List<string>();
            var unread = UnreadCount;
            if (unread > 0) parts.Add($"{unread} 条未读");
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        }
    }

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await RefreshCatalogAsync().ConfigureAwait(false);
        _catalog.StartWatching();
    }

    public async Task RefreshCatalogAsync()
    {
        ThrowIfDisposed();
        Interlocked.Exchange(ref _catalogRefreshRequested, 1);
        if (!await _catalogRefreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _catalogRefreshRequested, 0);
                var snapshot = await _catalog.ScanCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                if (!_shuttingDown) await RunOnUiAsync(() => ReplaceProjects(snapshot)).ConfigureAwait(false);
            } while (!_shuttingDown && Volatile.Read(ref _catalogRefreshRequested) != 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Active.ReportRecoverableError($"刷新会话失败：{exception.Message}");
        }
        finally
        {
            _catalogRefreshLock.Release();
            if (!_shuttingDown && Volatile.Read(ref _catalogRefreshRequested) != 0) _ = RefreshCatalogAfterChangeAsync();
        }
    }

    public async Task CreateSessionAsync(string cwd)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(cwd))
        {
            Active.ReportRecoverableError($"项目路径不存在：{cwd}");
            return;
        }

        var key = "new:" + Path.GetFullPath(cwd);
        if (_conversations.TryGetValue(key, out var existing))
        {
            Activate(existing);
            return;
        }

        var conversation = CreateConversation(key, "新对话");
        _conversations[key] = conversation;
        Activate(conversation);
        await conversation.StartNewAsync(cwd).ConfigureAwait(false);
        UpdateTreeState();
    }

    public async Task OpenSessionAsync(SessionSummary session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        if (!Directory.Exists(session.Cwd))
        {
            Active.ReportRecoverableError($"项目路径不存在：{session.Cwd}");
            return;
        }

        var key = Path.GetFullPath(session.SessionPath);
        if (_conversations.TryGetValue(key, out var existing))
        {
            Activate(existing);
            return;
        }

        var conversation = CreateConversation(key, session.Title);
        _conversations[key] = conversation;
        Activate(conversation);
        await conversation.OpenAsync(session).ConfigureAwait(false);
        UpdateTreeState();
    }

    private void Activate(ConversationViewModel conversation)
    {
        if (ReferenceEquals(Active, conversation))
        {
            conversation.HasUnread = false;
            return;
        }

        var previous = Active;
        Active = conversation;
        // A draft typed before any session was chosen follows into the first opened conversation.
        if (previous.Key == "initial" && (!string.IsNullOrWhiteSpace(previous.InputText) || previous.Attachments.Count > 0))
        {
            if (string.IsNullOrWhiteSpace(conversation.InputText)) conversation.InputText = previous.InputText;
            while (previous.Attachments.Count > 0 && conversation.Attachments.Count < ImageAttachmentViewModel.MaxImages)
            {
                var image = previous.Attachments[0];
                previous.Attachments.RemoveAt(0);
                conversation.Attachments.Add(image);
            }
        }

        conversation.HasUnread = false;
        foreach (var name in ForwardedPropertyNames)
        {
            OnPropertyChanged(name);
        }
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(BackgroundActivityText));
        UpdateTreeState();
    }

    private static readonly string[] ForwardedPropertyNames =
    [
        nameof(Messages), nameof(Attachments), nameof(SlashCommands), nameof(Models), nameof(CommandLoadStatus),
        nameof(InputText), nameof(CurrentTitle), nameof(CurrentCwd), nameof(ModelText), nameof(SelectedModel),
        nameof(StatusText), nameof(CanEditComposer), nameof(SelectedSessionPath), nameof(State), nameof(CanSend), nameof(CanSwitchSession),
        nameof(CanChangeModel), nameof(IsStreaming), nameof(CanReconnect), nameof(EmptyConversationText),
        nameof(MessageCountText), nameof(TotalTokensText), nameof(IsLoadingHistory),
        nameof(SendCommand), nameof(StopCommand), nameof(ReconnectCommand),
    ];

    private ConversationViewModel CreateConversation(string key, string title = "选择一个会话")
    {
        var conversation = new ConversationViewModel(key, _rpcClientFactory, _uiContext, title);
        conversation.KeyChanged += OnConversationKeyChanged;
        conversation.Attention += OnConversationAttention;
        conversation.PropertyChanged += OnConversationPropertyChanged;
        return conversation;
    }

    private void OnConversationKeyChanged(object? sender, (string OldKey, string NewKey) args)
    {
        if (sender is not ConversationViewModel conversation)
        {
            return;
        }

        _conversations.Remove(args.OldKey);
        _conversations[args.NewKey] = conversation;
        UpdateTreeState();
    }

    private void OnConversationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (sender is not ConversationViewModel conversation)
        {
            return;
        }

        if (ReferenceEquals(conversation, Active) && ForwardedPropertyNames.Contains(args.PropertyName))
        {
            OnPropertyChanged(args.PropertyName!);
        }

        if (args.PropertyName is nameof(ConversationViewModel.HasUnread) or nameof(ConversationViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(BackgroundActivityText));
            OnPropertyChanged(nameof(BusyCount));
            UpdateTreeState();
        }
    }

    private void OnConversationAttention(ConversationViewModel conversation, string kind)
    {
        if (!ReferenceEquals(conversation, Active))
        {
            conversation.HasUnread = true;
            OnPropertyChanged(nameof(UnreadCount));
            OnPropertyChanged(nameof(BackgroundActivityText));
        }

        UpdateTreeState();
        ConversationAttention?.Invoke(conversation, kind);
    }

    public Task SendAsync() => Active.SendAsync();
    public Task StopAsync() => Active.StopAsync();
    public Task SelectModelAsync(ModelOptionViewModel? model) => Active.SelectModelAsync(model);
    public Task ReconnectAsync() => Active.ReconnectAsync();

    public bool AddAttachment(ImageAttachmentViewModel image) => Active.AddAttachment(image);

    public void ReportRecoverableError(string message) => Active.ReportRecoverableError(message);

    /// <summary>Toggle the user's starred ("重点关注") marker for a session and persist it locally.</summary>
    public async Task ToggleStarAsync(SessionSummary session)
    {
        ThrowIfDisposed();
        var path = Path.GetFullPath(session.SessionPath);
        var starred = !_starredSessions.Contains(path);
        if (starred)
        {
            _starredSessions.Add(path);
        }
        else
        {
            _starredSessions.Remove(path);
        }

        UpdateTreeState();
        try
        {
            await _flagStore.SaveAsync(_starredSessions).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Active.ReportRecoverableError($"保存重点关注标记失败：{exception.Message}");
        }
    }

    public void RefreshRelativeActivityTimes(DateTimeOffset now)
    {
        foreach (var session in Projects.SelectMany(project => project.Sessions)) session.UpdateRelativeActivity(now);
    }

    internal void PrepareDocumentationDemo()
    {
        // Only synthetic conversation content is used for public screenshots.
        Active.PrepareDocumentationDemo();
        Messages.Clear();
        Messages.Add(new ChatItemViewModel(ChatItemKind.User, "帮我整理这个项目的改进计划。"));
        Messages.Add(new ChatItemViewModel(ChatItemKind.Assistant,
            "## 从本地对话开始\n\nPi Harbor 将本机 Pi 会话按项目集中展示，方便找到最近的工作并继续交流。\n\n" +
            "- **找回上下文**：查看历史文字、代码与工具结果。\n- **快速输入**：输入 @ 引用文件，输入 / 选择命令或 skill。\n- **图像交流**：粘贴截图，发送前点击缩略图检查。\n\n" +
            "### 下一步\n\n| 任务 | 状态 |\n| --- | --- |\n| 明确需求 | 已完成 |\n| 检查实现 | 进行中 |\n| 验证结果 | 待开始 |\n\n" +
            "> 这是公开截图的演示内容，不包含任何私人对话。"));
        for (var i = 0; i < 8; i++)
            Messages.Add(new ChatItemViewModel(ChatItemKind.System, $"演示步骤 {i + 1}：记录进展并核对结果，随时回到最新消息。"));
        InputText = "请结合这张截图，继续完善输入体验。";
    }

    public async Task ShutdownAsync()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _searchCts?.Cancel();
        await _searchTask.ConfigureAwait(false);
        _catalog.Changed -= OnCatalogChanged;
        _catalog.Dispose();
        foreach (var conversation in _conversations.Values.ToArray())
        {
            conversation.KeyChanged -= OnConversationKeyChanged;
            conversation.Attention -= OnConversationAttention;
            conversation.PropertyChanged -= OnConversationPropertyChanged;
            await conversation.DisposeAsync().ConfigureAwait(false);
        }
        _conversations.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(false);
        _disposed = true;
        _flagLock.Dispose();
        _catalogRefreshLock.Dispose();
    }

    private async Task LoadStarredSessionsAsync()
    {
        try
        {
            var starred = await _flagStore.LoadAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                _starredSessions.Clear();
                foreach (var path in starred) _starredSessions.Add(path);
                UpdateTreeState();
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Active.ReportRecoverableError($"读取重点关注标记失败：{exception.Message}");
        }
    }

    private void ReplaceProjects(CatalogSnapshot snapshot)
    {
        if (snapshot.Warnings.Count > 0 && State == ChatSessionState.Idle) Active.ReportRecoverableError(snapshot.Warnings[0]);
        var previous = Projects.SelectMany(project => project.Sessions).Select(item => item.Session);
        var incoming = snapshot.Projects.SelectMany(project => project.Sessions);
        if (previous.SequenceEqual(incoming)) return;
        var expansion = Projects.ToDictionary(project => project.Cwd, project => project.IsExpanded, StringComparer.OrdinalIgnoreCase);
        var latestPath = snapshot.Projects.SelectMany(project => project.Sessions).FirstOrDefault()?.SessionPath;
        Projects.Clear();
        foreach (var project in snapshot.Projects)
        {
            var item = new ProjectGroupViewModel(project)
            {
                IsExpanded = expansion.GetValueOrDefault(project.Cwd, true),
            };
            foreach (var session in item.Sessions) session.IsLatest = AreSameSessionPath(session.SessionPath, latestPath);
            Projects.Add(item);
        }
        UpdateTreeState();
        ScheduleSearch(catalogChanged: true);

        if (snapshot.Warnings.Count > 0 && State == ChatSessionState.Idle)
        {
            Active.ReportRecoverableError($"已加载 {snapshot.SessionCount} 个会话，跳过 {snapshot.SkippedFileCount} 个异常文件");
        }
    }

    /// <summary>Sync the sidebar tree with open conversations: current, busy, unread, starred and live title.</summary>
    private void UpdateTreeState()
    {
        foreach (var session in Projects.SelectMany(project => project.Sessions))
        {
            var sessionPath = Path.GetFullPath(session.SessionPath);
            session.IsCurrent = AreSameSessionPath(session.SessionPath, Active.Key);
            _conversations.TryGetValue(sessionPath, out var conversation);
            session.IsOpen = conversation is not null;
            session.IsBusy = conversation is { IsBusy: true };
            session.HasUnread = conversation is { HasUnread: true };
            session.IsStarred = _starredSessions.Contains(sessionPath);
            if (conversation is { Title.Length: > 0 } live && live.Title != "新对话" &&
                !_customRenames.Contains(sessionPath) && live.Title != session.Title)
            {
                session.UpdateTitle(live.Title);
            }
        }
    }

    private static bool AreSameSessionPath(string sessionPath, string? selectedSessionPath) =>
        !string.IsNullOrWhiteSpace(selectedSessionPath) &&
        !selectedSessionPath.StartsWith("new:", StringComparison.Ordinal) &&
        string.Equals(
            Path.GetFullPath(sessionPath),
            Path.GetFullPath(selectedSessionPath),
            StringComparison.OrdinalIgnoreCase);

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        _ = RefreshCatalogAfterChangeAsync();
    }

    private async Task RefreshCatalogAfterChangeAsync()
    {
        try
        {
            await RefreshCatalogAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private static string GetDefaultSessionRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pi", "agent", "sessions");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public sealed class ProjectGroupViewModel : ObservableObject
{
    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public ProjectGroupViewModel(ProjectGroup project)
    {
        Cwd = project.Cwd;
        DisplayName = project.DisplayName;
        LastActivityAt = project.LastActivityAt;
        Sessions = new ObservableCollection<SessionItemViewModel>(project.Sessions.Select(session =>
            new SessionItemViewModel(session)));
    }

    public string Cwd { get; }
    public string DisplayName { get; }
    public DateTimeOffset LastActivityAt { get; }
    public ObservableCollection<SessionItemViewModel> Sessions { get; }
}

public sealed class ModelOptionViewModel(string provider, string id, string name)
{
    public string Provider { get; } = provider;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Key => $"{Provider}/{Id}";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Key : $"{Name} · {Key}";
}

public sealed class SessionItemViewModel(SessionSummary session) : ObservableObject
{
    private SessionSummary _session = session;
    private bool _isCurrent;
    private bool _isOpen;
    private bool _isBusy;
    private bool _hasUnread;
    private bool _isStarred;
    private bool _isLatest;
    private string _relativeActivityText = RelativeActivityTime.Format(session.LastActivityAt, DateTimeOffset.Now);

    public SessionSummary Session
    {
        get => _session;
        private set => SetProperty(ref _session, value);
    }

    public void UpdateTitle(string title)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.Equals(Title, title, StringComparison.Ordinal))
        {
            Session = Session with { Title = title };
            OnPropertyChanged(nameof(ActivityToolTip));
        }
    }

    public bool IsExpanded { get; set; }
    public bool IsLatest { get => _isLatest; set => SetProperty(ref _isLatest, value); }
    public bool IsCurrent { get => _isCurrent; set => SetProperty(ref _isCurrent, value); }
    public bool IsOpen { get => _isOpen; set => SetProperty(ref _isOpen, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }
    public bool HasUnread { get => _hasUnread; set => SetProperty(ref _hasUnread, value); }
    public bool IsStarred { get => _isStarred; set => SetProperty(ref _isStarred, value); }

    public string RelativeActivityText => _relativeActivityText;
    public string ActivityToolTip => $"{Title}\n最后活动：{LastActivityAt.ToLocalTime():yyyy-MM-dd HH:mm}";
    public void UpdateRelativeActivity(DateTimeOffset now)
    {
        if (SetProperty(ref _relativeActivityText, RelativeActivityTime.Format(LastActivityAt, now), nameof(RelativeActivityText)))
            OnPropertyChanged(nameof(ActivityToolTip));
    }

    public string SessionPath => Session.SessionPath;
    public string Title => Session.Title;
    public string Cwd => Session.Cwd;
    public DateTimeOffset LastActivityAt => Session.LastActivityAt;

    public string StarMenuHeader => IsStarred ? "★ 取消重点关注" : "☆ 标为重点关注";
}

public sealed class ChatItemViewModel : ObservableObject
{
    private string _text;
    private string _title = string.Empty;
    private string _key = string.Empty;
    private bool _isError;
    private bool _isCompleted;
    private bool _isStreaming;
    private bool _isExpanded;
    private bool _isHighlighted;

    public ChatItemViewModel(ChatItemKind kind, string text)
    {
        Kind = kind;
        _text = text;
    }

    public ChatItemKind Kind { get; }
    public IReadOnlyList<ImageAttachmentViewModel> Images { get; init; } = [];

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Briefly set when a global search jumps to this message.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set => SetProperty(ref _isHighlighted, value);
    }

    public void Append(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Text += text;
        }
    }
}
