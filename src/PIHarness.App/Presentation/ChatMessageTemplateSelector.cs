// Created: 2026-09-06
// Purpose: Select a focused visual treatment for each conversation item category.

using System.Windows;
using System.Windows.Controls;
using PIHarness.App.ViewModels;
using PIHarness.Core.Models;

namespace PIHarness.App.Presentation;

public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ChatItemViewModel chatItem)
        {
            return base.SelectTemplate(item, container);
        }

        var key = chatItem.Kind switch
        {
            ChatItemKind.User => "UserMessageTemplate",
            ChatItemKind.Assistant => "AssistantMessageTemplate",
            ChatItemKind.Thinking => "ThinkingMessageTemplate",
            ChatItemKind.Tool => "ToolMessageTemplate",
            ChatItemKind.Error => "ErrorMessageTemplate",
            _ => "SystemMessageTemplate",
        };
        return container is FrameworkElement element
            ? element.TryFindResource(key) as DataTemplate
            : Application.Current.TryFindResource(key) as DataTemplate;
    }
}
