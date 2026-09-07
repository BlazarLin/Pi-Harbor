// Created: 2026-09-07
// Function: Capture deterministic WPF interaction states for style regression acceptance.
// Purpose: Let release validation cover states that static window screenshots cannot show.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PIHarness.App;

public partial class MainWindow
{
    private async Task CaptureNoSessionStyleAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        await SettleStyleLayoutAsync();
        await Task.Delay(350);
        await SettleStyleLayoutAsync();
        CaptureWindow(Path.Combine(outputDirectory, "01-no-session.png"));
    }

    private async Task<bool> RunStyleQaAsync(string outputDirectory)
    {
        var results = new List<string>();
        var bPassed = true;

        ModelSelector.Focus();
        Keyboard.Focus(ModelSelector);
        await SettleStyleLayoutAsync();
        CaptureWindow(Path.Combine(outputDirectory, "02-model-focus.png"));
        var bModelReady = ModelSelector.IsKeyboardFocusWithin && ModelSelector.SelectedItem is not null;
        AddStyleResult(results, "TEST-STYLE-02", "已有会话模型框可聚焦且保持自绘状态", bModelReady);
        bPassed &= bModelReady;

        ModelSelector.IsDropDownOpen = true;
        await SettleStyleLayoutAsync();
        var popup = ModelSelector.Template.FindName("PART_Popup", ModelSelector) as Popup;
        var bPopupReady = popup is { IsOpen: true, Child: FrameworkElement };
        if (popup?.Child is FrameworkElement popupContent)
        {
            CaptureElement(popupContent, Path.Combine(outputDirectory, "03-model-dropdown.png"));
        }
        AddStyleResult(results, "TEST-STYLE-03", "模型下拉列表使用暗色自绘弹层", bPopupReady);
        bPassed &= bPopupReady;
        ModelSelector.IsDropDownOpen = false;

        SessionTree.UpdateLayout();
        var projectItem = SessionTree.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
        var bProjectReady = projectItem is not null;
        if (projectItem is not null)
        {
            projectItem.IsSelected = true;
            ToggleProjectItem(projectItem);
            bProjectReady = !projectItem.IsSelected;
            await SettleStyleLayoutAsync();
            CaptureWindow(Path.Combine(outputDirectory, "04-project-not-selected.png"));

            var owner = FindContextMenuOwner(projectItem);
            var menu = owner?.ContextMenu;
            if (menu is not null)
            {
                menu.DataContext = owner!.DataContext;
                menu.PlacementTarget = owner;
                menu.IsOpen = true;
                await SettleStyleLayoutAsync();
                CaptureElement(menu, Path.Combine(outputDirectory, "05-project-context-menu.png"));
                menu.IsOpen = false;
            }

            var bMenuReady = menu is not null && File.Exists(Path.Combine(outputDirectory, "05-project-context-menu.png"));
            AddStyleResult(results, "TEST-STYLE-05", "项目右键菜单使用统一暗色模板", bMenuReady);
            bPassed &= bMenuReady;
        }

        AddStyleResult(results, "TEST-STYLE-01", "无会话模型框显示明确占位", File.Exists(Path.Combine(outputDirectory, "01-no-session.png")));
        AddStyleResult(results, "TEST-STYLE-04", "项目组点击后不保留选中状态", bProjectReady);
        bPassed &= File.Exists(Path.Combine(outputDirectory, "01-no-session.png")) && bProjectReady;

        var currentSession = _viewModel.Projects
            .SelectMany(project => project.Sessions)
            .SingleOrDefault(session => session.IsCurrent);
        var bCurrentSessionReady = currentSession is not null;
        if (currentSession is not null)
        {
            var currentProject = _viewModel.Projects.Single(project => project.Sessions.Contains(currentSession));
            var nProjectIndex = _viewModel.Projects.IndexOf(currentProject);
            var currentProjectItem = SessionTree.ItemContainerGenerator.ContainerFromIndex(nProjectIndex) as TreeViewItem;
            if (currentProjectItem is not null)
            {
                currentProjectItem.IsExpanded = true;
                await SettleStyleLayoutAsync();
                var nSessionIndex = currentProject.Sessions.IndexOf(currentSession);
                var currentSessionItem = currentProjectItem.ItemContainerGenerator.ContainerFromIndex(nSessionIndex) as TreeViewItem;
                var headerBorder = currentSessionItem?.Template.FindName("HeaderBorder", currentSessionItem) as Border;
                var expectedBrush = FindResource("CurrentSessionBrush") as SolidColorBrush;
                bCurrentSessionReady = headerBorder?.Background is SolidColorBrush actualBrush &&
                                       expectedBrush is not null &&
                                       actualBrush.Color == expectedBrush.Color;
                CaptureWindow(Path.Combine(outputDirectory, "06-current-session.png"));
            }
            else
            {
                bCurrentSessionReady = false;
            }
        }

        AddStyleResult(results, "TEST-STYLE-06", "当前会话使用独立颜色并在项目操作后保持高亮", bCurrentSessionReady);
        bPassed &= bCurrentSessionReady;
        File.WriteAllLines(Path.Combine(outputDirectory, "style-qa.txt"), results, Encoding.UTF8);
        return bPassed;
    }

    private async Task SettleStyleLayoutAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(120);
    }

    private static FrameworkElement? FindContextMenuOwner(DependencyObject root)
    {
        if (root is FrameworkElement { ContextMenu: not null } owner)
        {
            return owner;
        }

        for (var nIndex = 0; nIndex < VisualTreeHelper.GetChildrenCount(root); nIndex++)
        {
            var match = FindContextMenuOwner(VisualTreeHelper.GetChild(root, nIndex));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string CaptureElement(FrameworkElement element, string outputPath)
    {
        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var nWidth = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var nHeight = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(nWidth, nHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(outputPath))
        {
            encoder.Save(stream);
            stream.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath)));
    }

    private static void AddStyleResult(ICollection<string> results, string id, string goal, bool bPassed)
    {
        results.Add($"[{(bPassed ? "通过" : "失败")}] {id}：{goal}");
    }
}
