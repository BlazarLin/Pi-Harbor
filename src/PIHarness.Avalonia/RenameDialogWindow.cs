// Created: 2026-09-12
// Purpose: Minimal rename dialog for the Avalonia frontend (WPF RenameSessionDialog equivalent).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace PIHarness.Avalonia;

public sealed class RenameDialogWindow : Window
{
    private readonly TextBox _nameBox;

    public string NewName => _nameBox.Text?.Trim() ?? string.Empty;

    public RenameDialogWindow(string currentName)
    {
        Title = "重命名会话";
        Width = 420;
        MinHeight = 170;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _nameBox = new TextBox
        {
            Text = currentName,
            PlaceholderText = "输入新的会话名称",
            MaxLength = SessionNameLength,
        };

        var saveButton = new Button { Content = "保存", MinWidth = 90 };
        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(NewName))
            {
                return;
            }

            Close(true);
        };
        var cancelButton = new Button { Content = "取消", MinWidth = 90, Margin = new Thickness(10, 0, 0, 0) };
        cancelButton.Click += (_, _) => Close(false);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { saveButton, cancelButton },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "会话名称保存在本机，不修改 pi 的会话文件。", TextWrapping = TextWrapping.Wrap },
                new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { _nameBox } },
                buttonPanel,
            },
        };

        _nameBox.AttachedToVisualTree += (_, _) =>
        {
            _nameBox.Focus();
            _nameBox.SelectAll();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                Close(false);
            }
            else if (args.Key == Key.Enter)
            {
                saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
    }

    private const int SessionNameLength = 120;
}
