// Created: 2026-09-06
// Purpose: Verify that large history replacement produces one WPF collection refresh.

using System.Collections.Specialized;
using PIHarness.App.ViewModels;

namespace PIHarness.Tests;

internal static class BulkObservableCollectionTests
{
    [TestCase("TEST-16A", "批量替换历史只产生一次集合重置通知")]
    public static void ReplaceAllRaisesOneReset()
    {
        var collection = new BulkObservableCollection<int>();
        var nNotifications = 0;
        NotifyCollectionChangedAction? action = null;
        collection.CollectionChanged += (_, args) =>
        {
            nNotifications++;
            action = args.Action;
        };

        collection.ReplaceAll(Enumerable.Range(0, 1000));

        AssertEx.Equal(1000, collection.Count, "应一次装入全部历史项");
        AssertEx.Equal(1, nNotifications, "批量替换不得逐条通知 UI");
        AssertEx.Equal(NotifyCollectionChangedAction.Reset, action, "批量替换应发出 Reset");
    }
}
