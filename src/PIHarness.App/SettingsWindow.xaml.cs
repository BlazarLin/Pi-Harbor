// Created: 2026-09-08
// Purpose: Edit dialog for shell preferences; results are persisted by MainViewModel.

using System.Windows;
using PIHarness.App.ViewModels;

namespace PIHarness.App;

public partial class SettingsWindow : Window
{
    public bool CloseToTray { get; private set; }
    public bool ToastNotifications { get; private set; }
    public bool FlashWindow { get; private set; }
    public bool GlobalHotkey { get; private set; }
    public int IdleRecycleMinutes { get; private set; }

    public SettingsWindow(bool closeToTray, bool toastNotifications, bool flashWindow, bool globalHotkey, int idleRecycleMinutes)
    {
        InitializeComponent();
        CloseToTrayCheck.IsChecked = closeToTray;
        ToastCheck.IsChecked = toastNotifications;
        FlashCheck.IsChecked = flashWindow;
        HotkeyCheck.IsChecked = globalHotkey;
        IdleRecycleBox.Text = idleRecycleMinutes.ToString();
    }

    private void OnCancelClick(object sender, RoutedEventArgs args) => Close();

    private void OnSaveClick(object sender, RoutedEventArgs args)
    {
        if (!int.TryParse(IdleRecycleBox.Text.Trim(), out var idleRecycleMinutes) || idleRecycleMinutes < 0)
        {
            idleRecycleMinutes = 0;
        }

        CloseToTray = CloseToTrayCheck.IsChecked == true;
        ToastNotifications = ToastCheck.IsChecked == true;
        FlashWindow = FlashCheck.IsChecked == true;
        GlobalHotkey = HotkeyCheck.IsChecked == true;
        IdleRecycleMinutes = Math.Min(idleRecycleMinutes, MainViewModel.MaxIdleRecycleMinutes);
        DialogResult = true;
    }
}
