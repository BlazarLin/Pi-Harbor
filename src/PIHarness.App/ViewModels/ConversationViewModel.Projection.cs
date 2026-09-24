using System.Collections.Specialized;
using PIHarness.Core.Models;

namespace PIHarness.App.ViewModels;

public sealed partial class ConversationViewModel
{
    private ChatItemViewModel? _activityGroup;
    public BulkObservableCollection<ChatItemViewModel> DisplayMessages { get; } = [];

    private void OnProjectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
        {
            foreach (ChatItemViewModel item in args.NewItems) ProjectItem(item, DisplayMessages);
            return;
        }
        _activityGroup = null;
        var items = new List<ChatItemViewModel>();
        foreach (var item in Messages) ProjectItem(item, items);
        DisplayMessages.ReplaceAll(items);
    }

    private void ProjectItem(ChatItemViewModel item, ICollection<ChatItemViewModel> destination)
    {
        if (item.Kind == ChatItemKind.User)
        {
            _activityGroup?.CompleteActivity();
            _activityGroup = null;
        }
        if (item.Kind is ChatItemKind.Thinking or ChatItemKind.Tool)
        {
            if (_activityGroup is null)
            {
                _activityGroup = new ChatItemViewModel(ChatItemKind.ActivityGroup, "");
                if (_hasActiveTurn) _activityGroup.ExpandAutomatically();
                destination.Add(_activityGroup);
            }
            _activityGroup.Children.Add(item);
            _activityGroup.Title = $"思考与工具过程 · {_activityGroup.Children.Count} 步";
        }
        else
        {
            if (item.Kind == ChatItemKind.Metrics) _activityGroup?.CompleteActivity();
            destination.Add(item);
        }
    }
}
