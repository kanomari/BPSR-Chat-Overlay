using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.Models;
using BPSRChatOverlay.UIResources;
using Serilog;
using SharpPcap;
using SharpPcap.LibPcap;

namespace BPSRChatOverlay;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _currentConfig;
    private readonly NotificationSoundPlayer _notificationTestSoundPlayer = new();
    private readonly List<CaptureDeviceOption> _captureDeviceOptions = [];
    private string _worldChatTextColor;
    private string _channelChatTextColor;
    private string _partyChatTextColor;
    private string _guildChatTextColor;
    private string _newbieChatTextColor;
    private string _talkChatTextColor;
    private string _chatBackgroundColor;
    private string _menuBackgroundColor;
    private string _mentionHighlightColor;
    private string _talkHighlightBackgroundColor;

    public AppConfig? SavedConfig { get; private set; }

    public SettingsWindow(AppConfig currentConfig)
    {
        InitializeComponent();

        _currentConfig = currentConfig;
        CaptureDeviceComboBox.ItemsSource = _captureDeviceOptions;
        LoadCaptureDevices(currentConfig.CaptureDeviceName, true);
        _worldChatTextColor = NormalizeColorText(
            currentConfig.WorldChatTextColor,
            ChatColors.DefaultWorldChatTextColor);
        _channelChatTextColor = NormalizeColorText(
            currentConfig.ChannelChatTextColor,
            ChatColors.DefaultChannelChatTextColor);
        _partyChatTextColor = NormalizeColorText(
            currentConfig.PartyChatTextColor,
            ChatColors.DefaultPartyChatTextColor);
        _guildChatTextColor = NormalizeColorText(
            currentConfig.GuildChatTextColor,
            ChatColors.DefaultGuildChatTextColor);
        _newbieChatTextColor = NormalizeColorText(
            currentConfig.NewbieChatTextColor,
            ChatColors.DefaultNewbieChatTextColor);
        _talkChatTextColor = NormalizeColorText(
            currentConfig.TalkChatTextColor,
            ChatColors.DefaultTalkChatTextColor);
        _chatBackgroundColor = NormalizeColorText(
            currentConfig.ChatBackgroundColor,
            ChatColors.DefaultChatBackgroundColor);
        _menuBackgroundColor = NormalizeColorText(
            currentConfig.MenuBackgroundColor,
            ChatColors.DefaultMenuBackgroundColor);
        _mentionHighlightColor = NormalizeColorText(
            currentConfig.MentionHighlightColor,
            ChatColors.DefaultMentionHighlightColor);
        _talkHighlightBackgroundColor = NormalizeColorText(
            currentConfig.TalkHighlightBackgroundColor,
            ChatColors.DefaultTalkHighlightColor);

        FontSizeTextBox.Text =
            currentConfig.FontSize.ToString(CultureInfo.CurrentCulture);
        TimeColumnWidthTextBox.Text =
            currentConfig.TimeColumnWidth.ToString(CultureInfo.CurrentCulture);
        SenderNameColumnWidthTextBox.Text =
            currentConfig.SenderNameColumnWidth.ToString(
                CultureInfo.CurrentCulture);
        BackgroundOpacityTextBox.Text =
            currentConfig.BackgroundOpacity.ToString(CultureInfo.CurrentCulture);
        TextOpacityTextBox.Text =
            currentConfig.TextOpacity.ToString(CultureInfo.CurrentCulture);
        MenuBackgroundOpacityTextBox.Text =
            currentConfig.MenuBackgroundOpacity.ToString(
                CultureInfo.CurrentCulture);
        TopMostCheckBox.IsChecked = currentConfig.TopMost;
        ClickThroughCheckBox.IsChecked = currentConfig.ClickThrough;
        HighlightNewChatRowsCheckBox.IsChecked =
            currentConfig.HighlightNewChatRows;
        ShowChatSeparatorsCheckBox.IsChecked =
            currentConfig.ShowChatSeparators;
        ShowChatZebraStripesCheckBox.IsChecked =
            currentConfig.ShowChatZebraStripes;
        ShowChatColorBandCheckBox.IsChecked =
            currentConfig.ShowChatColorBand;
        ChatColorBandPositionComboBox.SelectedIndex =
            AppConfig.NormalizeChatColorBandPosition(
                currentConfig.ChatColorBandPosition) ==
            AppConfig.ChatColorBandPositionRight
                ? 1
                : 0;
        ShowWorldChatCheckBox.IsChecked = currentConfig.ShowWorldChat;
        ShowChannelChatCheckBox.IsChecked = currentConfig.ShowChannelChat;
        ShowPartyChatCheckBox.IsChecked = currentConfig.ShowPartyChat;
        ShowGuildChatCheckBox.IsChecked = currentConfig.ShowGuildChat;
        ShowNewbieChatCheckBox.IsChecked = currentConfig.ShowNewbieChat;
        ShowTalkChatCheckBox.IsChecked = currentConfig.ShowTalkChat;
        ChatFilterKeywordsTextBox.Text = currentConfig.ChatFilterKeywords;
        EnableMentionNotificationCheckBox.IsChecked =
            currentConfig.EnableMentionNotification;
        EnableMentionSoundCheckBox.IsChecked =
            currentConfig.EnableMentionSound;
        MentionKeywordsTextBox.Text =
            currentConfig.MentionKeywords ?? string.Empty;
        MentionSoundFilePathTextBox.Text =
            currentConfig.MentionSoundFilePath ?? string.Empty;
        EnableTalkHighlightCheckBox.IsChecked =
            currentConfig.EnableTalkHighlight;
        EnableTalkSoundCheckBox.IsChecked =
            currentConfig.EnableTalkSound;
        TalkSoundFilePathTextBox.Text =
            currentConfig.TalkSoundFilePath ?? string.Empty;
        ShowDebugPanelCheckBox.IsChecked =
            currentConfig.ShowDebugPanel;
        UpdateColorPreviews();
        CategoryListBox.SelectedIndex = 0;
    }

    private void CategoryListBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (DisplaySettingsPanel is null ||
            ChatSettingsPanel is null ||
            ColorSettingsPanel is null ||
            TalkSettingsPanel is null ||
            NetworkSettingsPanel is null ||
            AdvancedSettingsPanel is null ||
            AboutSettingsPanel is null)
        {
            return;
        }

        string? category =
            (CategoryListBox.SelectedItem as ListBoxItem)?.Tag as string;

        DisplaySettingsPanel.Visibility =
            category == "Display" ? Visibility.Visible : Visibility.Collapsed;
        ChatSettingsPanel.Visibility =
            category == "Chat" ? Visibility.Visible : Visibility.Collapsed;
        ColorSettingsPanel.Visibility =
            category == "Color" ? Visibility.Visible : Visibility.Collapsed;
        TalkSettingsPanel.Visibility =
            category == "Talk" ? Visibility.Visible : Visibility.Collapsed;
        NetworkSettingsPanel.Visibility =
            category == "Network" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedSettingsPanel.Visibility =
            category == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility =
            category == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsTitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // マウスボタンが解放済みの場合は移動を開始しません。
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

        if (!int.TryParse(
                TimeColumnWidthTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int timeColumnWidth) ||
            timeColumnWidth < AppConfig.MinTimeColumnWidth ||
            timeColumnWidth > AppConfig.MaxTimeColumnWidth)
        {
            MessageBox.Show(
                this,
                $"時刻列の幅は{AppConfig.MinTimeColumnWidth}～{AppConfig.MaxTimeColumnWidth}pxの範囲で入力してください。",
                "入力エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(
                SenderNameColumnWidthTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out int senderNameColumnWidth) ||
            senderNameColumnWidth < AppConfig.MinSenderNameColumnWidth ||
            senderNameColumnWidth > AppConfig.MaxSenderNameColumnWidth)
        {
            MessageBox.Show(
                this,
                $"名前列の幅は{AppConfig.MinSenderNameColumnWidth}～{AppConfig.MaxSenderNameColumnWidth}pxの範囲で入力してください。",
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
            CaptureDeviceName =
                (CaptureDeviceComboBox.SelectedItem as CaptureDeviceOption)?.Name
                ?? _currentConfig.CaptureDeviceName,
            ExeNames = [.. _currentConfig.ExeNames],
            FontSize = fontSize,
            TimeColumnWidth = timeColumnWidth,
            SenderNameColumnWidth = senderNameColumnWidth,
            BackgroundOpacity = backgroundOpacity,
            TextOpacity = textOpacity,
            MenuBackgroundOpacity = menuBackgroundOpacity,
            ClickThrough = ClickThroughCheckBox.IsChecked == true,
            HighlightNewChatRows =
                HighlightNewChatRowsCheckBox.IsChecked == true,
            ShowChatSeparators =
                ShowChatSeparatorsCheckBox.IsChecked == true,
            ShowChatZebraStripes =
                ShowChatZebraStripesCheckBox.IsChecked == true,
            ShowChatColorBand =
                ShowChatColorBandCheckBox.IsChecked == true,
            ChatColorBandPosition =
                (ChatColorBandPositionComboBox.SelectedItem as ComboBoxItem)
                    ?.Tag as string
                ?? AppConfig.ChatColorBandPositionLeft,
            WindowLeft = _currentConfig.WindowLeft,
            WindowTop = _currentConfig.WindowTop,
            WindowWidth = _currentConfig.WindowWidth,
            WindowHeight = _currentConfig.WindowHeight,
            ShowWorldChat = ShowWorldChatCheckBox.IsChecked == true,
            ShowChannelChat = ShowChannelChatCheckBox.IsChecked == true,
            ShowPartyChat = ShowPartyChatCheckBox.IsChecked == true,
            ShowGuildChat = ShowGuildChatCheckBox.IsChecked == true,
            ShowNewbieChat = ShowNewbieChatCheckBox.IsChecked == true,
            ShowTalkChat = ShowTalkChatCheckBox.IsChecked == true,
            ChatFilterKeywords = ChatFilterKeywordsTextBox.Text.Trim(),
            WorldChatTextColor = _worldChatTextColor,
            ChannelChatTextColor = _channelChatTextColor,
            PartyChatTextColor = _partyChatTextColor,
            GuildChatTextColor = _guildChatTextColor,
            NewbieChatTextColor = _newbieChatTextColor,
            TalkChatTextColor = _talkChatTextColor,
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
            EnableTalkHighlight =
                EnableTalkHighlightCheckBox.IsChecked == true,
            TalkHighlightBackgroundColor =
                _talkHighlightBackgroundColor,
            EnableTalkSound =
                EnableTalkSoundCheckBox.IsChecked == true,
            TalkSoundFilePath =
                TalkSoundFilePathTextBox.Text.Trim(),
            ShowDebugPanel = ShowDebugPanelCheckBox.IsChecked == true,
            TopMost = TopMostCheckBox.IsChecked == true
        };

        DialogResult = true;
    }

    private void ReloadCaptureDevicesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        string? selectedName =
            (CaptureDeviceComboBox.SelectedItem as CaptureDeviceOption)?.Name;

        LoadCaptureDevices(
            selectedName ?? _currentConfig.CaptureDeviceName,
            false);
    }

    private void LoadCaptureDevices(
        string? preferredDeviceName,
        bool isInitialLoad)
    {
        try
        {
            List<CaptureDeviceOption> loadedOptions =
                CaptureDeviceList.Instance
                    .Select(CreateCaptureDeviceOption)
                    .Where(option => !string.IsNullOrWhiteSpace(option.Name))
                    .ToList();

            EnsureUniqueDisplayNames(loadedOptions);

            _captureDeviceOptions.Clear();
            _captureDeviceOptions.AddRange(loadedOptions);
            CaptureDeviceComboBox.Items.Refresh();

            if (_captureDeviceOptions.Count == 0)
            {
                CaptureDeviceComboBox.SelectedItem = null;
                ShowCaptureDeviceStatus(
                    "利用可能なネットワークカードが見つかりません。");
                return;
            }

            CaptureDeviceOption? selectedOption =
                FindCaptureDevice(preferredDeviceName);

            if (selectedOption is null &&
                !string.Equals(
                    preferredDeviceName,
                    _currentConfig.CaptureDeviceName,
                    StringComparison.Ordinal))
            {
                selectedOption =
                    FindCaptureDevice(_currentConfig.CaptureDeviceName);
            }

            CaptureDeviceComboBox.SelectedItem =
                selectedOption ?? _captureDeviceOptions[0];

            bool savedDeviceMissing =
                !string.IsNullOrWhiteSpace(_currentConfig.CaptureDeviceName) &&
                FindCaptureDevice(_currentConfig.CaptureDeviceName) is null;

            if (savedDeviceMissing)
            {
                ShowCaptureDeviceStatus(
                    "保存されているネットワークカードが見つかりません。別のカードを選択してください。");
            }
            else
            {
                HideCaptureDeviceStatus();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading capture devices");

            if (isInitialLoad || _captureDeviceOptions.Count == 0)
            {
                CaptureDeviceComboBox.SelectedItem = null;
            }

            ShowCaptureDeviceStatus(
                "ネットワークカード一覧を取得できませんでした。Npcapが導入されているか確認してください。");
        }
    }

    private static CaptureDeviceOption CreateCaptureDeviceOption(
        ICaptureDevice device)
    {
        string name = device.Name ?? string.Empty;
        string? description = NormalizeDeviceText(device.Description);
        string? friendlyName = device is LibPcapLiveDevice liveDevice
            ? NormalizeDeviceText(liveDevice.Interface?.FriendlyName)
            : null;
        string displayName = CreateDisplayName(
            name,
            friendlyName,
            description);

        return new CaptureDeviceOption(
            name,
            friendlyName,
            description,
            displayName);
    }

    private static string CreateDisplayName(
        string name,
        string? friendlyName,
        string? description)
    {
        if (friendlyName is not null &&
            description is not null &&
            !string.Equals(
                friendlyName,
                description,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"{friendlyName} — {description}";
        }

        return friendlyName ?? description ?? name;
    }

    private static void EnsureUniqueDisplayNames(
        List<CaptureDeviceOption> options)
    {
        HashSet<string> duplicateDisplayNames = options
            .GroupBy(
                option => option.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < options.Count; index++)
        {
            CaptureDeviceOption option = options[index];
            if (duplicateDisplayNames.Contains(option.DisplayName))
            {
                options[index] = option with
                {
                    DisplayName = $"{option.DisplayName} — {option.Name}"
                };
            }
        }
    }

    private CaptureDeviceOption? FindCaptureDevice(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        return _captureDeviceOptions.FirstOrDefault(option =>
            string.Equals(
                option.Name,
                deviceName,
                StringComparison.Ordinal));
    }

    private static string? NormalizeDeviceText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void ShowCaptureDeviceStatus(string message)
    {
        CaptureDeviceStatusText.Text = message;
        CaptureDeviceStatusText.Visibility = Visibility.Visible;
    }

    private void HideCaptureDeviceStatus()
    {
        CaptureDeviceStatusText.Text = string.Empty;
        CaptureDeviceStatusText.Visibility = Visibility.Collapsed;
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
        byte alpha = settingName is
            nameof(AppConfig.MentionHighlightColor) or
            nameof(AppConfig.TalkHighlightBackgroundColor)
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
            nameof(AppConfig.NewbieChatTextColor) => _newbieChatTextColor,
            nameof(AppConfig.TalkChatTextColor) => _talkChatTextColor,
            nameof(AppConfig.ChatBackgroundColor) => _chatBackgroundColor,
            nameof(AppConfig.MenuBackgroundColor) => _menuBackgroundColor,
            nameof(AppConfig.MentionHighlightColor) =>
                _mentionHighlightColor,
            nameof(AppConfig.TalkHighlightBackgroundColor) =>
                _talkHighlightBackgroundColor,
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
            case nameof(AppConfig.NewbieChatTextColor):
                _newbieChatTextColor = colorText;
                break;
            case nameof(AppConfig.TalkChatTextColor):
                _talkChatTextColor = colorText;
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
            case nameof(AppConfig.TalkHighlightBackgroundColor):
                _talkHighlightBackgroundColor = colorText;
                break;
        }
    }

    private void UpdateColorPreviews()
    {
        SetPreviewColor(WorldChatTextColorPreview, _worldChatTextColor);
        SetPreviewColor(ChannelChatTextColorPreview, _channelChatTextColor);
        SetPreviewColor(PartyChatTextColorPreview, _partyChatTextColor);
        SetPreviewColor(GuildChatTextColorPreview, _guildChatTextColor);
        SetPreviewColor(NewbieChatTextColorPreview, _newbieChatTextColor);
        SetPreviewColor(TalkChatTextColorPreview, _talkChatTextColor);
        SetPreviewColor(ChatBackgroundColorPreview, _chatBackgroundColor);
        SetPreviewColor(MenuBackgroundColorPreview, _menuBackgroundColor);
        SetPreviewColor(
            MentionHighlightColorPreview,
            _mentionHighlightColor);
        SetPreviewColor(
            TalkHighlightBackgroundColorPreview,
            _talkHighlightBackgroundColor);
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
        var dialog = CreateSoundFileDialog();

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
        _notificationTestSoundPlayer.Play(
            MentionSoundFilePathTextBox.Text);
    }

    private static Microsoft.Win32.OpenFileDialog CreateSoundFileDialog()
    {
        return new Microsoft.Win32.OpenFileDialog
        {
            Filter =
                "音声ファイル (*.wav;*.mp3)|*.wav;*.mp3|" +
                "WAVファイル (*.wav)|*.wav|" +
                "MP3ファイル (*.mp3)|*.mp3|" +
                "すべてのファイル (*.*)|*.*"
        };
    }

    private void BrowseTalkSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog = CreateSoundFileDialog();

        if (dialog.ShowDialog(this) == true)
        {
            TalkSoundFilePathTextBox.Text =
                Path.GetFullPath(dialog.FileName);
        }
    }

    private void ClearTalkSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        TalkSoundFilePathTextBox.Text = string.Empty;
    }

    private void TestTalkSoundButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _notificationTestSoundPlayer.Play(
            TalkSoundFilePathTextBox.Text);
    }

    protected override void OnClosed(EventArgs e)
    {
        _notificationTestSoundPlayer.Dispose();
        base.OnClosed(e);
    }
}
