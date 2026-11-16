// Created: 2026-09-08
// Purpose: Open-conversation tab list, shell preferences and idle recycling of background
//          conversations so parallel pi processes do not accumulate forever.

using System.Collections.ObjectModel;
using PIHarness.Core.Sessions;

namespace PIHarness.App.ViewModels;

public sealed partial class MainViewModel
{
    internal const int MaxIdleRecycleMinutes = 240;

    private ShellPreferences? _preferences;
    private ConversationViewModel? _homeConversation;
    private System.Threading.Timer? _recycleTimer;

    /// <summary>Open conversations in activation order; rendered as the conversation tab strip.</summary>
    public ObservableCollection<ConversationViewModel> OpenConversations { get; } = [];

    public bool HasOpenConversations => OpenConversations.Count > 0;
    public int OpenConversationCount => OpenConversations.Count;

    private void NotifyOpenConversationsChanged()
    {
        OnPropertyChanged(nameof(HasOpenConversations));
        OnPropertyChanged(nameof(OpenConversationCount));
    }

    public bool CloseToTrayEnabled => _preferences?.CloseToTray ?? true;
    public bool ToastNotificationsEnabled => _preferences?.ToastNotifications ?? true;
    public bool FlashWindowEnabled => _preferences?.FlashWindow ?? true;
    public bool GlobalHotkeyEnabled => _preferences?.GlobalHotkeyEnabled ?? true;
    public int IdleRecycleMinutes => Math.Clamp(_preferences?.IdleRecycleMinutes ?? 10, 0, MaxIdleRecycleMinutes);
    public WindowLayoutData? WindowLayout => _preferences?.Window;

    public async Task UpdateShellPreferencesAsync(
        bool closeToTray, bool toastNotifications, bool flashWindow, bool globalHotkey, int idleRecycleMinutes)
    {
        ThrowIfDisposed();
        await _managementLoaded.ConfigureAwait(false);
        _preferences = new ShellPreferences(
            closeToTray,
            toastNotifications,
            flashWindow,
            globalHotkey,
            Math.Clamp(idleRecycleMinutes, 0, MaxIdleRecycleMinutes),
            _preferences?.Window);
        NotifyPreferencesChanged();
        await _managementSaved.ConfigureAwait(false);
        await SaveManagementAsync().ConfigureAwait(false);
    }

    public async Task SaveWindowLayoutAsync(WindowLayoutData layout)
    {
        if (_shuttingDown) return;
        try { await _managementLoaded.ConfigureAwait(false); } catch (ObjectDisposedException) { return; }
        _preferences = (_preferences ?? new ShellPreferences()) with { Window = layout };
        await SaveManagementAsync().ConfigureAwait(false);
    }

    private void NotifyPreferencesChanged()
    {
        OnPropertyChanged(nameof(CloseToTrayEnabled));
        OnPropertyChanged(nameof(ToastNotificationsEnabled));
        OnPropertyChanged(nameof(FlashWindowEnabled));
        OnPropertyChanged(nameof(GlobalHotkeyEnabled));
        OnPropertyChanged(nameof(IdleRecycleMinutes));
        OnPropertyChanged(nameof(WindowLayout));
    }

    /// <summary>Bring an already-open conversation back to the main chat surface (tab click).</summary>
    public void ActivateConversation(ConversationViewModel conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (_conversations.Values.Contains(conversation) || ReferenceEquals(conversation, _homeConversation))
        {
            Activate(conversation);
        }
    }

    public string ExportActiveConversationMarkdown() => Active.ExportMarkdown();

    public void RevokeQueuedMessage(ChatItemViewModel item) => Active.RevokeQueued(item);

    /// <summary>Close a conversation tab, retiring its pi process. Busy conversations are refused.</summary>
    public async Task CloseConversationAsync(ConversationViewModel conversation)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.IsBusy)
        {
            Active.ReportRecoverableError("会话仍在处理，请先停止生成或等它结束，再关闭这个标签页。");
            return;
        }

        if (ReferenceEquals(conversation, Active))
        {
            var next = OpenConversations.Where(item => !ReferenceEquals(item, conversation)).LastOrDefault();
            Activate(next ?? _homeConversation ?? conversation);
        }

        _conversations.Remove(conversation.Key);
        conversation.KeyChanged -= OnConversationKeyChanged;
        conversation.Attention -= OnConversationAttention;
        conversation.PropertyChanged -= OnConversationPropertyChanged;
        OpenConversations.Remove(conversation);
        OnPropertyChanged(nameof(BusyCount));
        OnPropertyChanged(nameof(BackgroundActivityText));
        UpdateTreeState();
        await conversation.DisposeAsync().ConfigureAwait(false);
    }

    internal async Task RecycleIdleConversationsAsync(DateTimeOffset now)
    {
        var conversations = _conversations.Values.ToArray();
        foreach (var conversation in conversations)
        {
            var threshold = TimeSpan.FromMinutes(IdleRecycleMinutes);
            if (threshold <= TimeSpan.Zero || ReferenceEquals(conversation, Active) ||
                conversation.IsBusy || conversation.HasUnread ||
                now - conversation.LastActivityAt < threshold)
            {
                continue;
            }

            await CloseConversationAsync(conversation).ConfigureAwait(false);
        }
    }

    private void StartRecycleTimer()
    {
        if (_uiContext is null) return; // Tests drive RecycleIdleConversationsAsync directly.
        _recycleTimer = new System.Threading.Timer(
            _ => _ = RecycleIdleConversationsAsync(DateTimeOffset.Now),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }
}
