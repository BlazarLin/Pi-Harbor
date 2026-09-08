using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PIHarness.Core.Sessions;

namespace PIHarness.App.Presentation;

public sealed class RenameSessionDialog : Window
{
    public string? NewName { get; private set; }
    internal TextBox NameBox { get; }
    public RenameSessionDialog(Window owner, string name)
    {
        Owner = owner;
        Title = "重命名对话";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PanelBrush");
        Foreground = (Brush)FindResource("PrimaryTextBrush");
        FontFamily = owner.FontFamily;
        FontSize = 14;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "对话名称", FontWeight = FontWeights.SemiBold });
        NameBox = new TextBox
        {
            Text = name, MaxLength = SessionNameStore.MaxNameLength, Margin = new Thickness(0, 12, 0, 8), Padding = new Thickness(10),
            Background = (Brush)FindResource("WindowBrush"), Foreground = Foreground,
            CaretBrush = (Brush)FindResource("AccentBrush"), BorderBrush = (Brush)FindResource("BorderBrush"),
        };
        content.Children.Add(NameBox);
        content.Children.Add(new TextBlock { Text = "名称仅用于本机 Pi Harbor 的列表和搜索，最多 120 个字符。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush") });
        var error = new TextBlock { Foreground = (Brush)FindResource("ErrorBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        content.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var restore = new Button { Content = "恢复默认", Style = (Style)FindResource("FlatButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        restore.Click += (_, _) => { NewName = null; DialogResult = true; };
        var cancel = new Button { Content = "取消", IsCancel = true, Style = (Style)FindResource("FlatButtonStyle"), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "保存", IsDefault = true, Style = (Style)FindResource("AccentButtonStyle"), Padding = new Thickness(20, 8, 20, 8) };
        save.Click += (_, _) =>
        {
            try { NewName = SessionNameStore.Validate(NameBox.Text); DialogResult = true; }
            catch (ArgumentException exception) { error.Text = exception.Message; NameBox.Focus(); }
        };
        buttons.Children.Add(restore); buttons.Children.Add(cancel); buttons.Children.Add(save);
        content.Children.Add(buttons);
        Content = content;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }
}
