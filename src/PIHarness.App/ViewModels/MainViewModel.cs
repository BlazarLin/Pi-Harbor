// Created: 2026-09-06
// Purpose: Coordinate the local session catalog, one pi RPC process, and chat presentation state.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using PIHarness.Core.Models;
using PIHarness.Core.Rpc;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public enum ChatSessionState
{
    Idle,
    Starting,
    Ready,
    Streaming,
    Stopping,
    Faulted,
}

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly string _sessionRoot;
    private readonly Func<PiRpcClient> _rpcClientFactory;
    private readonly SessionCatalog _catalog;
    private readonly SemaphoreSlim _catalogRefreshLock = new(1, 1);
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<int, ChatItemViewModel> _streamItems = new();
    private readonly Dictionary<string, ChatItemViewModel> _toolItems = new(StringComparer.Ordinal);
    private readonly Stopwatch _turnTimer = new();
    private PiRpcClient? _rpcClient;
    private string _inputText = string.Empty;
    private string _currentTitle = "选择一个会话";
    private string _currentCwd = string.Empty;
    private string _modelText = "";
    private string _statusText = "就绪";
    private string? _selectedSessionPath;
    private ChatSessionState _state = ChatSessionState.Idle;
    private bool _isLoadingHistory;
    private bool _disposed;
    private bool _shuttingDown;
    private bool _hasActiveTurn;
    private TokenUsage _activeTurnUsage;

    public MainViewModel(string? sessionRoot = null, Func<PiRpcClient>? rpcClientFactory = null)
    {
        _sessionRoot = Path.GetFullPath(sessionRoot ?? GetDefaultSessionRoot());
        _rpcClientFactory = rpcClientFactory ?? (() => new PiRpcClient());
        _catalog = new SessionCatalog(_sessionRoot);
        _catalog.Changed += OnCatalogChanged;
        _uiContext = SynchronizationContext.Current;

        RefreshCommand = new AsyncRelayCommand(RefreshCatalogAsync, () => State is not ChatSessionState.Starting);
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        StopCommand = new AsyncRelayCommand(StopAsync, () => State == ChatSessionState.Streaming);
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(MessageCountText));
    }

    public ObservableCollection<ProjectGroupViewModel> Projects { get; } = [];
    public BulkObservableCollection<ChatItemViewModel> Messages { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public AsyncRelayCommand StopCommand { get; }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                OnPropertyChanged(nameof(CanSend));
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    public string CurrentCwd
    {
        get => _currentCwd;
        private set => SetProperty(ref _currentCwd, value);
    }

    public string ModelText
    {
        get => _modelText;
        private set => SetProperty(ref _modelText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? SelectedSessionPath
    {
        get => _selectedSessionPath;
        private set => SetProperty(ref _selectedSessionPath, value);
    }

    public ChatSessionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            StatusText = value switch
            {
                ChatSessionState.Idle => "就绪",
                ChatSessionState.Starting => "正在启动 pi…",
                ChatSessionState.Ready => "就绪",
                ChatSessionState.Streaming => "pi 正在处理…",
                ChatSessionState.Stopping => "正在停止…",
                ChatSessionState.Faulted => "发生错误",
                _ => string.Empty,
            };
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSwitchSession));
            OnPropertyChanged(nameof(IsStreaming));
            RefreshCommand.RaiseCanExecuteChanged();
            SendCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanSend => State == ChatSessionState.Ready && !string.IsNullOrWhiteSpace(InputText);
    public bool CanSwitchSession => State is ChatSessionState.Idle or ChatSessionState.Ready or ChatSessionState.Faulted;
    public bool IsStreaming => State is ChatSessionState.Streaming or ChatSessionState.Stopping;
    public string MessageCountText => Messages.Count == 0 ? "尚无消息" : $"{Messages.Count} 项";

    public bool IsLoadingHistory
    {
        get => _isLoadingHistory;
        private set => SetProperty(ref _isLoadingHistory, value);
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
        if (!await _catalogRefreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var snapshot = await SessionCatalog.ScanAsync(_sessionRoot, CancellationToken.None).ConfigureAwait(false);
            await RunOnUiAsync(() => ReplaceProjects(snapshot)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync($"刷新会话失败：{exception.Message}").ConfigureAwait(false);
        }
        finally
        {
            _catalogRefreshLock.Release();
        }
    }

    public async Task CreateSessionAsync(string cwd)
    {
        ThrowIfDisposed();
        if (!CanSwitchSession)
        {
            return;
        }

        if (!Directory.Exists(cwd))
        {
            await ShowErrorAsync($"项目路径不存在：{cwd}").ConfigureAwait(false);
            return;
        }

        await CloseRpcClientAsync().ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            Messages.Clear();
            CurrentTitle = "新对话";
            CurrentCwd = Path.GetFullPath(cwd);
            SelectedSessionPath = null;
            ModelText = string.Empty;
            IsLoadingHistory = false;
            ResetActiveTurn();
        }).ConfigureAwait(false);
        await StartRpcAsync(new PiStartOptions(Path.GetFullPath(cwd))).ConfigureAwait(false);
    }

    public async Task OpenSessionAsync(SessionSummary session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        if (!CanSwitchSession)
        {
            return;
        }

        if (!Directory.Exists(session.Cwd))
        {
            await ShowErrorAsync($"项目路径不存在：{session.Cwd}").ConfigureAwait(false);
            return;
        }

        await CloseRpcClientAsync().ConfigureAwait(false);
        await RunOnUiAsync(() =>
        {
            Messages.Clear();
            CurrentTitle = session.Title;
            CurrentCwd = session.Cwd;
            SelectedSessionPath = session.SessionPath;
            ModelText = string.Empty;
            IsLoadingHistory = true;
            ResetActiveTurn();
        }).ConfigureAwait(false);
        var historyTask = SessionHistoryReader.ReadAsync(session.SessionPath, CancellationToken.None);
        await StartRpcAsync(new PiStartOptions(session.Cwd, session.SessionPath), historyTask).ConfigureAwait(false);
    }

    public async Task SendAsync()
    {
        ThrowIfDisposed();
        if (!CanSend || _rpcClient is null)
        {
            return;
        }

        var message = InputText.Trim();
        await RunOnUiAsync(() =>
        {
            InputText = string.Empty;
            Messages.Add(new ChatItemViewModel(ChatItemKind.User, message));
            _streamItems.Clear();
            _toolItems.Clear();
            _activeTurnUsage = default;
            _hasActiveTurn = true;
            _turnTimer.Restart();
            State = ChatSessionState.Streaming;
        }).ConfigureAwait(false);

        try
        {
            await _rpcClient.SendPromptAsync(message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PiRpcException or IOException)
        {
            await RunOnUiAsync(CompleteActiveTurn).ConfigureAwait(false);
            await ShowErrorAsync($"发送消息失败：{exception.Message}").ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        ThrowIfDisposed();
        if (State != ChatSessionState.Streaming || _rpcClient is null)
        {
            return;
        }

        await RunOnUiAsync(() => State = ChatSessionState.Stopping).ConfigureAwait(false);
        try
        {
            await _rpcClient.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            await RunOnUiAsync(() => State = ChatSessionState.Ready).ConfigureAwait(false);
        }
        catch (PiRpcException exception)
        {
            await ShowErrorAsync($"停止生成失败：{exception.Message}").ConfigureAwait(false);
        }
    }

    public async Task ShutdownAsync()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _catalog.Changed -= OnCatalogChanged;
        _catalog.Dispose();
        await CloseRpcClientAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ShutdownAsync().ConfigureAwait(false);
        _disposed = true;
        _catalogRefreshLock.Dispose();
    }

    private async Task StartRpcAsync(
        PiStartOptions options,
        Task<SessionHistorySnapshot>? historyTask = null)
    {
        await RunOnUiAsync(() => State = ChatSessionState.Starting).ConfigureAwait(false);
        var client = _rpcClientFactory();
        client.EventReceived += OnRpcEventReceived;
        client.Exited += OnRpcExited;
        _rpcClient = client;

        try
        {
            await client.StartAsync(options, CancellationToken.None).ConfigureAwait(false);
            var stateTask = client.RequestAsync(
                "get_state",
                null,
                TimeSpan.FromMinutes(2),
                CancellationToken.None);
            if (historyTask is not null)
            {
                await ApplyLocalHistoryAsync(historyTask).ConfigureAwait(false);
            }

            var stateResponse = await stateTask.ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ApplyStateResponse(stateResponse);
                State = ChatSessionState.Ready;
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PiRpcException or IOException or UnauthorizedAccessException)
        {
            await RunOnUiAsync(() => IsLoadingHistory = false).ConfigureAwait(false);
            await ShowErrorAsync($"打开 pi 会话失败：{exception.Message}").ConfigureAwait(false);
        }
    }

    private async Task CloseRpcClientAsync()
    {
        var client = _rpcClient;
        _rpcClient = null;
        if (client is null)
        {
            return;
        }

        client.EventReceived -= OnRpcEventReceived;
        client.Exited -= OnRpcExited;
        await client.DisposeAsync().ConfigureAwait(false);
    }

    private void ApplyStateResponse(JsonElement response)
    {
        if (!response.TryGetProperty("data", out var data))
        {
            return;
        }

        if (data.TryGetProperty("sessionName", out var name) && name.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(name.GetString()))
        {
            CurrentTitle = name.GetString()!;
        }

        if (data.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            var provider = ReadString(model, "provider");
            var id = ReadString(model, "id");
            ModelText = string.IsNullOrWhiteSpace(provider) ? id : $"{provider}/{id}";
        }
    }

    private async Task ApplyLocalHistoryAsync(Task<SessionHistorySnapshot> historyTask)
    {
        try
        {
            var snapshot = await historyTask.ConfigureAwait(false);
            var items = snapshot.Items.Select(CreateHistoryViewModel).ToArray();
            await RunOnUiAsync(() => Messages.ReplaceAll(items)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            await RunOnUiAsync(() => Messages.ReplaceAll(
                [new ChatItemViewModel(ChatItemKind.Error, $"读取本地历史失败：{exception.Message}")])).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsLoadingHistory = false).ConfigureAwait(false);
        }
    }

    private static ChatItemViewModel CreateHistoryViewModel(SessionHistoryItem item) =>
        new(item.Kind, item.Text)
        {
            Title = item.Title,
            Key = item.Key,
            IsError = item.IsError,
            IsCompleted = item.Kind == ChatItemKind.Tool,
        };

    private void OnRpcEventReceived(object? sender, PiRpcEvent rpcEvent)
    {
        _ = HandleRpcEventAsync(rpcEvent);
    }

    private async Task HandleRpcEventAsync(PiRpcEvent rpcEvent)
    {
        try
        {
            await RunOnUiAsync(() => ApplyRpcEvent(rpcEvent)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            await ShowErrorAsync($"处理 pi 事件失败：{exception.Message}").ConfigureAwait(false);
        }
    }

    private void ApplyRpcEvent(PiRpcEvent rpcEvent)
    {
        switch (rpcEvent.Type)
        {
            case "agent_start":
                State = ChatSessionState.Streaming;
                break;
            case "agent_settled":
                CompleteStreamItems();
                CompleteActiveTurn();
                State = ChatSessionState.Ready;
                break;
            case "message_start":
                if (rpcEvent.Payload.TryGetProperty("message", out var startMessage) && ReadString(startMessage, "role") == "assistant")
                {
                    _streamItems.Clear();
                }
                break;
            case "message_update":
                ApplyMessageUpdate(rpcEvent.Payload);
                break;
            case "message_end":
                ApplyMessageEnd(rpcEvent.Payload);
                break;
            case "tool_execution_start":
            case "tool_execution_update":
            case "tool_execution_end":
                ApplyToolEvent(rpcEvent.Type, rpcEvent.Payload);
                break;
            case "extension_error":
                Messages.Add(new ChatItemViewModel(ChatItemKind.Error, $"扩展错误：{rpcEvent.Payload}"));
                break;
        }
    }

    private void ApplyMessageUpdate(JsonElement payload)
    {
        if (!payload.TryGetProperty("assistantMessageEvent", out var deltaEvent))
        {
            return;
        }

        var eventType = ReadString(deltaEvent, "type");
        var contentIndex = deltaEvent.TryGetProperty("contentIndex", out var indexElement) && indexElement.TryGetInt32(out var nIndex)
            ? nIndex
            : -1;
        if (contentIndex < 0)
        {
            return;
        }

        switch (eventType)
        {
            case "text_start":
                GetOrCreateStreamItem(contentIndex, ChatItemKind.Assistant);
                break;
            case "text_delta":
                GetOrCreateStreamItem(contentIndex, ChatItemKind.Assistant).Append(ReadString(deltaEvent, "delta"));
                break;
            case "thinking_start":
                GetOrCreateStreamItem(contentIndex, ChatItemKind.Thinking);
                break;
            case "thinking_delta":
                GetOrCreateStreamItem(contentIndex, ChatItemKind.Thinking).Append(ReadString(deltaEvent, "delta"));
                break;
            case "toolcall_start":
            {
                var toolName = ReadString(deltaEvent, "toolName");
                var toolId = ReadString(deltaEvent, "id");
                var item = GetOrCreateStreamItem(contentIndex, ChatItemKind.Tool);
                item.Title = $"工具：{toolName}";
                item.Key = toolId;
                if (!string.IsNullOrWhiteSpace(toolId))
                {
                    _toolItems[toolId] = item;
                }
                break;
            }
            case "toolcall_delta":
                GetOrCreateStreamItem(contentIndex, ChatItemKind.Tool).Append(ReadString(deltaEvent, "delta"));
                break;
        }
    }

    private void ApplyMessageEnd(JsonElement payload)
    {
        if (!payload.TryGetProperty("message", out var message))
        {
            return;
        }

        _activeTurnUsage += ReadUsage(message);
        if (ReadString(message, "role") != "assistant")
        {
            return;
        }

        var stopReason = ReadString(message, "stopReason");
        if (stopReason == "error")
        {
            CompleteStreamItems();
            var errorMessage = ReadString(message, "errorMessage");
            Messages.Add(new ChatItemViewModel(
                ChatItemKind.Error,
                string.IsNullOrWhiteSpace(errorMessage) ? "模型返回未知错误。" : errorMessage));
            return;
        }

        if (stopReason == "aborted")
        {
            Messages.Add(new ChatItemViewModel(ChatItemKind.System, "已停止生成。"));
        }

        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            CompleteStreamItems();
            return;
        }

        var nIndex = 0;
        foreach (var part in content.EnumerateArray())
        {
            var type = ReadString(part, "type");
            if (type == "text")
            {
                GetOrCreateStreamItem(nIndex, ChatItemKind.Assistant).Text = ReadString(part, "text");
            }
            else if (type == "thinking")
            {
                GetOrCreateStreamItem(nIndex, ChatItemKind.Thinking).Text = ReadString(part, "thinking");
            }
            else if (type == "toolCall")
            {
                var item = GetOrCreateStreamItem(nIndex, ChatItemKind.Tool);
                var name = ReadString(part, "name");
                var id = ReadString(part, "id");
                item.Title = $"工具：{name}";
                item.Key = id;
                if (!item.IsCompleted)
                {
                    item.Text = part.TryGetProperty("arguments", out var arguments) ? arguments.ToString() : string.Empty;
                }
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _toolItems[id] = item;
                }
            }

            nIndex++;
        }

        CompleteStreamItems();
    }

    private void ApplyToolEvent(string eventType, JsonElement payload)
    {
        var id = ReadString(payload, "toolCallId");
        var name = ReadString(payload, "toolName");
        if (!_toolItems.TryGetValue(id, out var item))
        {
            item = new ChatItemViewModel(ChatItemKind.Tool, string.Empty) { Key = id, IsStreaming = true };
            _toolItems[id] = item;
            Messages.Add(item);
        }

        item.Title = eventType == "tool_execution_end" ? $"工具完成：{name}" : $"正在执行：{name}";
        if (eventType == "tool_execution_start" && payload.TryGetProperty("args", out var args))
        {
            item.Text = args.ToString();
        }
        else if (eventType == "tool_execution_update" && payload.TryGetProperty("partialResult", out var partialResult))
        {
            item.Text = ReadResultText(partialResult);
        }
        else if (eventType == "tool_execution_end")
        {
            item.Text = payload.TryGetProperty("result", out var result) ? ReadResultText(result) : string.Empty;
            item.IsError = payload.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True;
            item.IsCompleted = true;
            item.IsStreaming = false;
        }
    }

    private ChatItemViewModel GetOrCreateStreamItem(int nContentIndex, ChatItemKind kind)
    {
        if (_streamItems.TryGetValue(nContentIndex, out var item))
        {
            return item;
        }

        item = new ChatItemViewModel(kind, string.Empty) { IsStreaming = true };
        _streamItems[nContentIndex] = item;
        Messages.Add(item);
        return item;
    }

    private void CompleteStreamItems()
    {
        foreach (var item in _streamItems.Values)
        {
            item.IsStreaming = false;
        }
    }

    private void CompleteActiveTurn()
    {
        if (!_hasActiveTurn)
        {
            return;
        }

        _turnTimer.Stop();
        var metrics = new TurnMetrics(_activeTurnUsage, _turnTimer.Elapsed);
        Messages.Add(new ChatItemViewModel(ChatItemKind.Metrics, metrics.ToDisplayText()));
        _hasActiveTurn = false;
    }

    private void ResetActiveTurn()
    {
        _turnTimer.Reset();
        _activeTurnUsage = default;
        _hasActiveTurn = false;
    }

    private static string ReadContentText(JsonElement container)
    {
        if (!container.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return content.ToString();
        }

        return string.Join(
            Environment.NewLine,
            content.EnumerateArray()
                .Where(part => ReadString(part, "type") == "text")
                .Select(part => ReadString(part, "text"))
                .Where(text => !string.IsNullOrEmpty(text)));
    }

    private static string ReadResultText(JsonElement result) => ReadContentText(result);

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static TokenUsage ReadUsage(JsonElement message)
    {
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        return new TokenUsage(
            ReadInt64(usage, "input"),
            ReadInt64(usage, "output"),
            ReadInt64(usage, "cacheRead"),
            ReadInt64(usage, "cacheWrite"),
            ReadInt64(usage, "reasoning"),
            ReadInt64(usage, "totalTokens"));
    }

    private static long ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : 0;

    private void ReplaceProjects(CatalogSnapshot snapshot)
    {
        Projects.Clear();
        foreach (var project in snapshot.Projects)
        {
            Projects.Add(new ProjectGroupViewModel(project));
        }

        if (snapshot.Warnings.Count > 0 && State == ChatSessionState.Idle)
        {
            StatusText = $"已加载 {snapshot.SessionCount} 个会话，跳过 {snapshot.SkippedFileCount} 个异常文件";
        }
    }

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

    private void OnRpcExited(object? sender, int exitCode)
    {
        if (!_shuttingDown && State is not ChatSessionState.Idle)
        {
            _ = ShowErrorAsync($"pi RPC 进程已退出，退出码：{exitCode}。");
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        await RunOnUiAsync(() =>
        {
            Messages.Add(new ChatItemViewModel(ChatItemKind.Error, message));
            State = ChatSessionState.Faulted;
            StatusText = message;
        }).ConfigureAwait(false);
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

public sealed class ProjectGroupViewModel
{
    public ProjectGroupViewModel(ProjectGroup project)
    {
        Cwd = project.Cwd;
        DisplayName = project.DisplayName;
        LastActivityAt = project.LastActivityAt;
        Sessions = new ObservableCollection<SessionItemViewModel>(project.Sessions.Select(session => new SessionItemViewModel(session)));
    }

    public string Cwd { get; }
    public string DisplayName { get; }
    public DateTimeOffset LastActivityAt { get; }
    public ObservableCollection<SessionItemViewModel> Sessions { get; }
}

public sealed class SessionItemViewModel(SessionSummary session)
{
    public SessionSummary Session { get; } = session;
    public string SessionPath => Session.SessionPath;
    public string Title => Session.Title;
    public string Cwd => Session.Cwd;
    public DateTimeOffset LastActivityAt => Session.LastActivityAt;
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

    public ChatItemViewModel(ChatItemKind kind, string text)
    {
        Kind = kind;
        _text = text;
    }

    public ChatItemKind Kind { get; }

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

    public void Append(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Text += text;
        }
    }
}
