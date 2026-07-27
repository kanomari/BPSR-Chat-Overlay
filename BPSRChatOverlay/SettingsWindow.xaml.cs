using System.Globalization;
using System.Windows;
using BPSRChatOverlay.Config;

namespace BPSRChatOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _currentConfig;

    public AppConfig? SavedConfig { get; private set; }

    public SettingsWindow(AppConfig currentConfig)
    {
        InitializeComponent();

        _currentConfig = currentConfig;

        FontSizeTextBox.Text =
            currentConfig.FontSize.ToString(CultureInfo.CurrentCulture);
        BackgroundOpacityTextBox.Text =
            currentConfig.BackgroundOpacity.ToString(CultureInfo.CurrentCulture);
        TextOpacityTextBox.Text =
            currentConfig.TextOpacity.ToString(CultureInfo.CurrentCulture);
        MenuBackgroundOpacityTextBox.Text =
            currentConfig.MenuBackgroundOpacity.ToString(
                CultureInfo.CurrentCulture);
        TopMostCheckBox.IsChecked = currentConfig.TopMost;
        ClickThroughCheckBox.IsChecked = currentConfig.ClickThrough;
        ShowWorldChatCheckBox.IsChecked = currentConfig.ShowWorldChat;
        ShowChannelChatCheckBox.IsChecked = currentConfig.ShowChannelChat;
        ShowPartyChatCheckBox.IsChecked = currentConfig.ShowPartyChat;
        ShowGuildChatCheckBox.IsChecked = currentConfig.ShowGuildChat;
        ChatFilterKeywordsTextBox.Text = currentConfig.ChatFilterKeywords;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(
                FontSizeTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int fontSize))
        {
            MessageBox.Show(
                this,
                "フォントサイズには整数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (fontSize < 8 || fontSize > 48)
        {
            MessageBox.Show(
                this,
                "フォントサイズは8～48の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(
                BackgroundOpacityTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double backgroundOpacity))
        {
            MessageBox.Show(
                this,
                "背景不透明度には小数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.IsFinite(backgroundOpacity) ||
            backgroundOpacity < 0.0 ||
            backgroundOpacity > 1.0)
        {
            MessageBox.Show(
                this,
                "背景不透明度は0.0～1.0の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(
                TextOpacityTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double textOpacity))
        {
            MessageBox.Show(
                this,
                "文字不透明度には小数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.IsFinite(textOpacity) ||
            textOpacity < 0.0 ||
            textOpacity > 1.0)
        {
            MessageBox.Show(
                this,
                "文字不透明度は0.0～1.0の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(
                MenuBackgroundOpacityTextBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double menuBackgroundOpacity))
        {
            MessageBox.Show(
                this,
                "メニューバー背景不透明度には小数を入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!double.IsFinite(menuBackgroundOpacity) ||
            menuBackgroundOpacity < 0.0 ||
            menuBackgroundOpacity > 1.0)
        {
            MessageBox.Show(
                this,
                "メニューバー背景不透明度は0.0～1.0の範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SavedConfig = new AppConfig
        {
            CaptureDeviceName = _currentConfig.CaptureDeviceName,
            ExeNames = [.. _currentConfig.ExeNames],
            FontSize = fontSize,
            BackgroundOpacity = backgroundOpacity,
            TextOpacity = textOpacity,
            MenuBackgroundOpacity = menuBackgroundOpacity,
            ClickThrough = ClickThroughCheckBox.IsChecked == true,
            WindowLeft = _currentConfig.WindowLeft,
            WindowTop = _currentConfig.WindowTop,
            WindowWidth = _currentConfig.WindowWidth,
            WindowHeight = _currentConfig.WindowHeight,
            ShowWorldChat = ShowWorldChatCheckBox.IsChecked == true,
            ShowChannelChat = ShowChannelChatCheckBox.IsChecked == true,
            ShowPartyChat = ShowPartyChatCheckBox.IsChecked == true,
            ShowGuildChat = ShowGuildChatCheckBox.IsChecked == true,
            ChatFilterKeywords = ChatFilterKeywordsTextBox.Text.Trim(),
            TopMost = TopMostCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
