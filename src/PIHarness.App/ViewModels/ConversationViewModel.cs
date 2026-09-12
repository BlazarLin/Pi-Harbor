// Created: 2026-09-08
// Purpose: Own one conversation (RPC process, chat items, drafts, models and token usage) so several
//          sessions can stream in parallel while the user keeps another conversation in view.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using PIHarness.App.Presentation;
using PIHarness.Core.Models;
using PIHarness.Core.Rpc;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public sealed class ConversationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly Func<PiRpcClient> _rpcClientFactory;
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<int, ChatItemViewModel> _streamItems = new();
    private readonly Dictionary<string, ChatItemViewModel> _toolItems = new(StringComparer.Ordinal);
    private readonly Stopwatch _turnTimer = new();
    private PiRpcClient? _rpcClient;
    private string _inputText = string.Empty;
    private string _title;
    private string _cwd = string.Empty;
    private string _modelText = "";
    private string _statusText = "就绪";
    private ModelOptionViewModel? _selectedModel;
    private ChatSessionState _state = ChatSessionState.Idle;
    private bool _isLoadingHistory;
    private bool _disposed;
    private bool _hasActiveTurn;
    private TokenUsage _activeTurnUsage;
    private TokenUsage _totalUsage;
    private bool _isSending;
    private bool _hasUnread;

    public ConversationViewModel(string key, Func<PiRpcClient> rpcClientFactory, SynchronizationContext? uiContext, string title = "选择一个会话")
    {
        Key = key;
        _rpcClientFactory = rpcClientFactory;
        _uiContext = uiContext;
        _title = title;
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
        StopCommand = new AsyncRelayCommand(StopAsync, () => State == ChatSessionState.Streaming);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => CanReconnect);
    }

    /// <summary>Identity inside <see cref="MainViewModel"/>: the session file path, or "new:cwd" before pi persists one.</summary>
    public string Key { get; private set; }
    public string? SessionPath { get; private set; }

    public event EventHandler<(string OldKey, string NewKey)>? KeyChanged;

    /// <summary>Raised when a background turn settles or the pi process fails, so the shell can notify the user.</summary>
    public event Action<ConversationViewModel, string>? Attention;

    public BulkObservableCollection<ChatItemViewModel> Messages { get; } = [];
    public ObservableCollection<ImageAttachmentViewModel> Attachments { get; } = [];
    public ObservableCollection<ComposerSuggestion> SlashCommands { get; } = [];
    public ObservableCollection<ModelOptionViewModel> Models { get; } = [];
    public string CommandLoadStatus { get; private set; } = "选择会话后加载 Pi 命令与 skills";

    public AsyncRelayCommand SendCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }

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

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Cwd
    {
        get => _cwd;
        private set => SetProperty(ref _cwd, value);
    }

    public string ModelText
    {
        get => _modelText;
        private set => SetProperty(ref _modelText, value);
    }

    public ModelOptionViewModel? SelectedModel
    {
        get => _selectedModel;
        set => SetProperty(ref _selectedModel, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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
                ChatSessionState.SwitchingModel => "正在切换模型…",
                ChatSessionState.Streaming => "pi 正在处理…",
                ChatSessionState.Stopping => "正在停止…",
                ChatSessionState.Faulted => "发生错误",
                _ => string.Empty,
            };
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSwitchSession));
            OnPropertyChanged(nameof(CanChangeModel));
            OnPropertyChanged(nameof(IsStreaming));
            OnPropertyChanged(nameof(CanReconnect));
            OnPropertyChanged(nameof(EmptyConversationText));
            SendCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ReconnectCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanSend => !_isSending && State == ChatSessionState.Ready && (!string.IsNullOrWhiteSpace(InputText) || Attachments.Count > 0);
    public bool CanSwitchSession => !_isSending;
    public bool CanChangeModel => !_isSending && State == ChatSessionState.Ready && Models.Count > 0;
    public bool IsStreaming => State is ChatSessionState.Streaming or ChatSessionState.Stopping;
    public bool IsBusy => State is ChatSessionState.Streaming or ChatSessionState.Stopping or ChatSessionState.Starting or ChatSessionState.SwitchingModel;
    public bool CanReconnect => CanSwitchSession && State == ChatSessionState.Faulted && Directory.Exists(Cwd);
    public string EmptyConversationText => State switch
    {
        ChatSessionState.Ready => "在下方输入消息，开始这个对话",
        ChatSessionState.Starting => "正在准备对话…",
        _ => "选择左侧会话，或新建一个对话",
    };
    public string MessageCountText => Messages.Count == 0 ? "尚无消息" : $"{Messages.Count} 项";

    /// <summary>Accumulated tokens of the loaded history plus every turn finished in this app run.</summary>
    public TokenUsage TotalUsage
    {
        get => _totalUsage;
        private set
        {
            if (SetProperty(ref _totalUsage, value))
            {
                OnPropertyChanged(nameof(TotalTokensText));
            }
        }
    }

    public string TotalTokensText => $"累计 Tokens {_totalUsage.TotalTokens:N0}";

    public bool HasUnread
    {
        get => _hasUnread;
        set => SetProperty(ref _hasUnread, value);
    }

    public bool IsLoadingHistory
    {
        get => _isLoadingHistory;
        private set => SetProperty(ref _isLoadingHistory, value);
    }

    public async Task StartNewAsync(string cwd)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(cwd))
        {
            StatusText = $"项目路径不存在：{cwd}";
            return;
        }

        await RunOnUiAsync(() =>
        {
            State = ChatSessionState.Starting;
            Messages.Clear();
            Title = "新对话";
            Cwd = Path.GetFullPath(cwd);
            ModelText = string.Empty;
            Models.Clear();
            SelectedModel = null;
            IsLoadingHistory = false;
            ResetActiveTurn();
        }).ConfigureAwait(false);
        await StartRpcAsync(new PiStartOptions(Path.GetFullPath(cwd))).ConfigureAwait(false);
    }

    public async Task OpenAsync(SessionSummary session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ThrowIfDisposed();
        if (!Directory.Exists(session.Cwd))
        {
            StatusText = $"项目路径不存在：{session.Cwd}";
            return;
        }

        await RunOnUiAsync(() =>
        {
            State = ChatSessionState.Starting;
            Messages.Clear();
            Title = session.Title;
            Cwd = session.Cwd;
            ModelText = string.Empty;
            Models.Clear();
            SelectedModel = null;
            IsLoadingHistory = true;
            ResetActiveTurn();
        }).ConfigureAwait(false);
        var historyTask = SessionHistoryReader.ReadAsync(session.SessionPath, CancellationToken.None);
        await StartRpcAsync(new PiStartOptions(session.Cwd, session.SessionPath), historyTask).ConfigureAwait(false);
    }

    public async Task SendAsync()
    {
        ThrowIfDisposed();
        var client = _rpcClient;
        if (!CanSend || client is null)
        {
            return;
        }

        var message = InputText.Trim();
        var images = Attachments.ToArray();
        if (message.Length == 0) message = "请分析这些图片。";
        var sentItem = new ChatItemViewModel(ChatItemKind.User, message) { Images = images };
        await RunOnUiAsync(() =>
        {
            _isSending = true;
            OnPropertyChanged(nameof(CanEditComposer));
            OnPropertyChanged(nameof(CanSwitchSession));
            Messages.Add(sentItem);
            _streamItems.Clear();
            _toolItems.Clear();
            _activeTurnUsage = default;
            _hasActiveTurn = true;
            _turnTimer.Restart();
            State = ChatSessionState.Streaming;
        }).ConfigureAwait(false);

        try
        {
            await client.SendPromptAsync(message, images.Select(image => image.ToPromptImage()).ToArray(), CancellationToken.None).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                InputText = string.Empty;
                Attachments.Clear();
            }).ConfigureAwait(false);
            // An extension can handle a command without starting an agent turn.
            if (message.StartsWith('/'))
            {
                JsonElement state;
                try { state = await client.RequestAsync("get_state", CancellationToken.None).ConfigureAwait(false); }
                catch (Exception exception) when (exception is PiRpcException or IOException or ObjectDisposedException)
                {
                    await ShowErrorAsync($"命令已接受，但无法确认运行状态：{exception.Message}。请重新连接会话。").ConfigureAwait(false);
                    return;
                }
                await RunOnUiAsync(() =>
                {
                    if (state.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("isStreaming", out var streaming) && !streaming.GetBoolean() && _hasActiveTurn)
                    {
                        ResetActiveTurn();
                        State = ChatSessionState.Ready;
                    }
                }).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is PiRpcException or IOException or ObjectDisposedException)
        {
            await RunOnUiAsync(() =>
            {
                ResetActiveTurn();
                if (!string.IsNullOrEmpty(InputText) || Attachments.Count > 0) Messages.Remove(sentItem);
                State = ChatSessionState.Faulted;
                StatusText = $"发送未确认：{exception.Message}。草稿已保留；重新连接会话核对历史后再发送。";
                Messages.Add(new ChatItemViewModel(ChatItemKind.Error, StatusText));
            }).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                _isSending = false;
                OnPropertyChanged(nameof(CanEditComposer));
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(CanSwitchSession));
                OnPropertyChanged(nameof(CanChangeModel));
                OnPropertyChanged(nameof(CanReconnect));
                ReconnectCommand.RaiseCanExecuteChanged();
                SendCommand.RaiseCanExecuteChanged();
            }).ConfigureAwait(false);
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

    public Task ReconnectAsync()
    {
        if (!CanReconnect) return Task.CompletedTask;
        return SessionPath is { } path
            ? OpenAsync(new SessionSummary(path, Cwd, Title, DateTimeOffset.Now, DateTimeOffset.Now))
            : StartNewAsync(Cwd);
    }

    public async Task SelectModelAsync(ModelOptionViewModel? model)
    {
        ThrowIfDisposed();
        if (model is null || !CanChangeModel || _rpcClient is null ||
            string.Equals(model.Key, ModelText, StringComparison.Ordinal))
        {
            return;
        }

        var previous = SelectedModel;
        await RunOnUiAsync(() => State = ChatSessionState.SwitchingModel).ConfigureAwait(false);
        try
        {
            var response = await _rpcClient.RequestAsync(
                "set_model",
                new Dictionary<string, object?>
                {
                    ["provider"] = model.Provider,
                    ["modelId"] = model.Id,
                },
                TimeSpan.FromSeconds(30),
                CancellationToken.None).ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ApplySelectedModel(response, model);
                State = ChatSessionState.Ready;
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PiRpcException or IOException)
        {
            await RunOnUiAsync(() =>
            {
                SelectedModel = previous;
                State = ChatSessionState.Ready;
                StatusText = $"模型切换失败：{exception.Message}";
                Messages.Add(new ChatItemViewModel(ChatItemKind.Error, StatusText));
            }).ConfigureAwait(false);
        }
    }

    public bool CanEditComposer => !_isSending;

    public bool AddAttachment(ImageAttachmentViewModel image)
    {
        if (!CanEditComposer) return false;
        if (Attachments.Count >= ImageAttachmentViewModel.MaxImages)
        {
            ReportRecoverableError("每条消息最多添加 4 张图片，请移除后再添加。");
            return false;
        }
        Attachments.Add(image);
        return true;
    }

    public void ReportRecoverableError(string message)
    {
        StatusText = message;
    }

    /// <summary>A local rename overrides the title previously read from pi.</summary>
    internal void ApplySessionRename(string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
    }

    // Synthetic presentation state for public documentation screenshots; never touches a pi process.
    internal void PrepareDocumentationDemo()
    {
        Title = "Pi Harbor 使用演示";
        Cwd = "示例对话 · 左侧真实项目名称与会话标题已遮挡";
        StatusText = "本地会话自动发现已开启";
        ModelText = "";
        var demoModel = new ModelOptionViewModel("demo", "model", "模型示例");
        Models.Add(demoModel);
        SelectedModel = demoModel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await CloseRpcClientAsync().ConfigureAwait(false);
    }

    private async Task StartRpcAsync(
        PiStartOptions options,
        Task<SessionHistorySnapshot>? historyTask = null)
    {
        await RunOnUiAsync(() => State = ChatSessionState.Starting).ConfigureAwait(false);
        // Reconnects reuse this conversation; the previous process must be retired first.
        await CloseRpcClientAsync().ConfigureAwait(false);
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
            var modelsTask = RequestAvailableModelsAsync(client);
            var commandsTask = RequestCommandsAsync(client);
            if (historyTask is not null)
            {
                await ApplyLocalHistoryAsync(historyTask).ConfigureAwait(false);
            }

            var stateResponse = await stateTask.ConfigureAwait(false);
            var modelsResponse = await modelsTask.ConfigureAwait(false);
            var commandsResponse = await commandsTask.ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ApplyStateResponse(stateResponse);
                ApplyAvailableModels(modelsResponse);
                ApplyCommands(commandsResponse);
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
            Title = name.GetString()!;
        }

        if (data.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            var provider = ReadString(model, "provider");
            var id = ReadString(model, "id");
            ModelText = string.IsNullOrWhiteSpace(provider) ? id : $"{provider}/{id}";
        }

        var sessionFile = ReadString(data, "sessionFile");
        if (!string.IsNullOrWhiteSpace(sessionFile))
        {
            var fullSessionFile = Path.GetFullPath(sessionFile);
            if (!string.Equals(SessionPath, fullSessionFile, StringComparison.OrdinalIgnoreCase))
            {
                var oldKey = Key;
                SessionPath = fullSessionFile;
                Key = fullSessionFile;
                KeyChanged?.Invoke(this, (oldKey, fullSessionFile));
            }
        }
    }

    private static async Task<JsonElement?> RequestCommandsAsync(PiRpcClient client)
    {
        try { return await client.RequestAsync("get_commands", null, TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is PiRpcException or IOException) { return null; }
    }

    private void ApplyCommands(JsonElement? response)
    {
        SlashCommands.Clear();
        CommandLoadStatus = "当前 Pi 未提供命令；可继续输入文字";
        if (response is not { } value || !value.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array) return;
        foreach (var command in commands.EnumerateArray())
        {
            var name = ReadString(command, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var source = ReadString(command, "source");
            var category = source == "skill" ? "Skill" : source == "prompt" ? "提示模板" : "扩展命令";
            SlashCommands.Add(new ComposerSuggestion("/" + name, category + " · " + ReadString(command, "description"), "/" + name + " "));
        }
        CommandLoadStatus = SlashCommands.Count == 0 ? "当前会话没有可用的命令或 skills" : "未找到匹配的命令或 skill";
    }

    private static async Task<JsonElement?> RequestAvailableModelsAsync(PiRpcClient client)
    {
        try
        {
            return await client.RequestAsync(
                "get_available_models",
                null,
                TimeSpan.FromMinutes(2),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PiRpcException or IOException)
        {
            return null;
        }
    }

    private void ApplyAvailableModels(JsonElement? response)
    {
        Models.Clear();
        if (response.HasValue && response.Value.TryGetProperty("data", out var data) &&
            data.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in models.EnumerateArray())
            {
                var provider = ReadString(item, "provider");
                var id = ReadString(item, "id");
                if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(id))
                {
                    Models.Add(new ModelOptionViewModel(provider, id, ReadString(item, "name")));
                }
            }
        }

        var current = Models.FirstOrDefault(model => string.Equals(model.Key, ModelText, StringComparison.Ordinal));
        if (current is null && TrySplitModelKey(ModelText, out var fallbackProvider, out var fallbackId))
        {
            current = new ModelOptionViewModel(fallbackProvider, fallbackId, string.Empty);
            Models.Insert(0, current);
        }

        SelectedModel = current;
        OnPropertyChanged(nameof(CanChangeModel));
    }

    private void ApplySelectedModel(JsonElement response, ModelOptionViewModel fallback)
    {
        var selected = fallback;
        if (response.TryGetProperty("data", out var data))
        {
            var provider = ReadString(data, "provider");
            var id = ReadString(data, "id");
            if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(id))
            {
                selected = Models.FirstOrDefault(model => model.Provider == provider && model.Id == id)
                    ?? new ModelOptionViewModel(provider, id, ReadString(data, "name"));
            }
        }

        SelectedModel = selected;
        ModelText = selected.Key;
    }

    private static bool TrySplitModelKey(string key, out string provider, out string id)
    {
        var separator = key.IndexOf('/');
        if (separator > 0 && separator < key.Length - 1)
        {
            provider = key[..separator];
            id = key[(separator + 1)..];
            return true;
        }

        provider = string.Empty;
        id = string.Empty;
        return false;
    }

    private async Task ApplyLocalHistoryAsync(Task<SessionHistorySnapshot> historyTask)
    {
        try
        {
            var snapshot = await historyTask.ConfigureAwait(false);
            var items = snapshot.Items.Select(CreateHistoryViewModel).ToArray();
            var total = new TokenUsage(
                snapshot.Items.Sum(item => item.Metrics?.Input ?? 0),
                snapshot.Items.Sum(item => item.Metrics?.Output ?? 0),
                snapshot.Items.Sum(item => item.Metrics?.CacheRead ?? 0),
                snapshot.Items.Sum(item => item.Metrics?.CacheWrite ?? 0),
                snapshot.Items.Sum(item => item.Metrics?.Reasoning ?? 0),
                snapshot.Items.Sum(item => item.Metrics?.TotalTokens ?? 0));
            await RunOnUiAsync(() =>
            {
                Messages.ReplaceAll(items);
                TotalUsage = total;
            }).ConfigureAwait(false);
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
        TotalUsage += _activeTurnUsage;
        _hasActiveTurn = false;
        Attention?.Invoke(this, "settled");
    }

    private void ResetActiveTurn()
    {
        _turnTimer.Reset();
        _activeTurnUsage = default;
        _hasActiveTurn = false;
    }

    private void OnRpcExited(object? sender, int exitCode)
    {
        if (_disposed || State == ChatSessionState.Idle)
        {
            return;
        }

        _ = RunOnUiAsync(() =>
        {
            State = ChatSessionState.Faulted;
            StatusText = $"pi RPC 进程已退出，退出码：{exitCode}。";
            Messages.Add(new ChatItemViewModel(ChatItemKind.Error, StatusText));
        }).ConfigureAwait(false);
        Attention?.Invoke(this, "failed");
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

    private static string ReadResultText(JsonElement result) => ReadContentText(result);

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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
