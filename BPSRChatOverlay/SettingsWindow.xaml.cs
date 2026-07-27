using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.UIResources;

namespace BPSRChatOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _currentConfig;
    private readonly MentionSoundPlayer _mentionTestSoundPlayer = new();
    private string _worldChatTextColor;
    private string _channelChatTextColor;
    private string _partyChatTextColor;
    private string _guildChatTextColor;
    private string _chatBackgroundColor;
    private string _menuBackgroundColor;
    private string _mentionHighlightColor;

    public AppConfig? SavedConfig { get; private set; }

    public SettingsWindow(AppConfig currentConfig)
    {
        InitializeComponent();

        _currentConfig = currentConfig;
        _worldChatTextColor = NormalizeColorText(
            currentConfig.WorldChatTextColor,
            ChatColors.DefaultChatTextColor);
        _channelChatTextColor = NormalizeColorText(
            currentConfig.ChannelChatTextColor,
            ChatColors.DefaultChatTextColor);
        _partyChatTextColor = NormalizeColorText(
            currentConfig.PartyChatTextColor,
            ChatColors.DefaultChatTextColor);
        _guildChatTextColor = NormalizeColorText(
            currentConfig.GuildChatTextColor,
            ChatColors.DefaultChatTextColor);
        _chatBackgroundColor = NormalizeColorText(
            currentConfig.ChatBackgroundColor,
            ChatColors.DefaultChatBackgroundColor);
        _menuBackgroundColor = NormalizeColorText(
            currentConfig.MenuBackgroundColor,
            ChatColors.DefaultMenuBackgroundColor);
        _mentionHighlightColor = NormalizeColorText(
            currentConfig.MentionHighlightColor,
            ChatColors.DefaultMentionHighlightColor);

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
        EnableMentionNotificationCheckBox.IsChecked =
            currentConfig.EnableMentionNotification;
        EnableMentionSoundCheckBox.IsChecked =
            currentConfig.EnableMentionSound;
        MentionKeywordsTextBox.Text =
            currentConfig.MentionKeywords ?? string.Empty;
        MentionSoundFilePathTextBox.Text =
            currentConfig.MentionSoundFilePath ?? string.Empty;
        ShowDebugPanelCheckBox.IsChecked =
            currentConfig.ShowDebugPanel;
        UpdateColorPreviews();
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
            WorldChatTextColor = _worldChatTextColor,
            ChannelChatTextColor = _channelChatTextColor,
            PartyChatTextColor = _partyChatTextColor,
            GuildChatTextColor = _guildChatTextColor,
            ChatBackgroundColor = _chatBackgroundColor,
            MenuBackgroundColor = _menuBackgroundColor,
            EnableMentionNotification =
                EnableMentionNotificationCheckBox.IsChecked == true,
            EnableMentionSound =
                EnableMentionSoundCheckBox.IsChecked == true,
            MentionKeywords = MentionKeywordsTextBox.Text.Trim(),
            MentionHighlightColor = _mentionHighlightColor,
            MentionSoundFilePath =
                MentionSoundFilePathTextBox.Text.Trim(),
            ShowDebugPanel = ShowDebugPanelCheckBox.IsChecked == true,
            TopMost = TopMostCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string settingName })
        {
            return;
        }

        string currentColor = GetTemporaryColor(settingName);
        Color wpfColor =
            (Color)ColorConverter.ConvertFromString(currentColor);

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            Color = System.Drawing.Color.FromArgb(
                wpfColor.R,
                wpfColor.G,
                wpfColor.B),
            FullOpen = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        System.Drawing.Color selectedColor = dialog.Color;
        byte alpha = settingName == nameof(AppConfig.MentionHighlightColor)
            ? wpfColor.A
            : byte.MaxValue;
        string colorText =
            $"#{alpha:X2}{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";

        SetTemporaryColor(settingName, colorText);
        UpdateColorPreviews();
    }

    private string GetTemporaryColor(string settingName)
    {
        return settingName switch
        {
            nameof(AppConfig.WorldChatTextColor) => _worldChatTextColor,
            nameof(AppConfig.ChannelChatTextColor) => _channelChatTextColor,
            nameof(AppConfig.PartyChatTextColor) => _partyChatTextColor,
            nameof(AppConfig.GuildChatTextColor) => _guildChatTextColor,
            nameof(AppConfig.ChatBackgroundColor) => _chatBackgroundColor,
            nameof(AppConfig.MenuBackgroundColor) => _menuBackgroundColor,
            nameof(AppConfig.MentionHighlightColor) =>
                _mentionHighlightColor,
            _ => ChatColors.DefaultChatTextColor
        };
    }

    private void SetTemporaryColor(string settingName, string colorText)
    {
        switch (settingName)
        {
            case nameof(AppConfig.WorldChatTextColor):
                _worldChatTextColor = colorText;
                break;
            case nameof(AppConfig.ChannelChatTextColor):
                _channelChatTextColor = colorText;
                break;
            case nameof(AppConfig.PartyChatTextColor):
                _partyChatTextColor = colorText;
                break;
            case nameof(AppConfig.GuildChatTextColor):
                _guildChatTextColor = colorText;
                break;
            case nameof(AppConfig.ChatBackgroundColor):
                _chatBackgroundColor = colorText;
                break;
            case nameof(AppConfig.MenuBackgroundColor):
                _menuBackgroundColor = colorText;
                break;
            case nameof(AppConfig.MentionHighlightColor):
                _mentionHighlightColor = colorText;
                break;
        }
    }

    private void UpdateColorPreviews()
    {
        SetPreviewColor(WorldChatTextColorPreview, _worldChatTextColor);
        SetPreviewColor(ChannelChatTextColorPreview, _channelChatTextColor);
        SetPreviewColor(PartyChatTextColorPreview, _partyChatTextColor);
        SetPreviewColor(GuildChatTextColorPreview, _guildChatTextColor);
        SetPreviewColor(ChatBackgroundColorPreview, _chatBackgroundColor);
        SetPreviewColor(MenuBackgroundColorPreview, _menuBackgroundColor);
        SetPreviewColor(
            MentionHighlightColorPreview,
            _mentionHighlightColor);
    }

    private static void SetPreviewColor(Border preview, string colorText)
    {
        preview.Background = ChatColors.CreateBrush(
            colorText,
            ChatColors.DefaultChatTextColor);
    }

    private static string NormalizeColorText(
        string? colorText,
        string fallbackColor)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return fallbackColor;
        }

        try
        {
            Color color =
                (Color)ColorConverter.ConvertFromString(colorText);
            return
                $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return fallbackColor;
        }
    }

    private void BrowseMentionSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter =
                "音声ファイル (*.wav;*.mp3)|*.wav;*.mp3|" +
                "WAVファイル (*.wav)|*.wav|" +
                "MP3ファイル (*.mp3)|*.mp3|" +
                "すべてのファイル (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            MentionSoundFilePathTextBox.Text =
                Path.GetFullPath(dialog.FileName);
        }
    }

    private void ClearMentionSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MentionSoundFilePathTextBox.Text = string.Empty;
    }

    private void TestMentionSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _mentionTestSoundPlayer.Play(
            MentionSoundFilePathTextBox.Text);
    }

    protected override void OnClosed(EventArgs e)
    {
        _mentionTestSoundPlayer.Dispose();
        base.OnClosed(e);
    }
}
