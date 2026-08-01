using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.Hotkeys;
using BPSRChatOverlay.Managers;
using BPSRChatOverlay.Models;
using BPSRChatOverlay.UIResources;
using BPSRChatOverlay.Updates;
using BPSR_ZDPSLib;
using Serilog;

namespace BPSRChatOverlay;

public partial class MainWindow : Window
{
    private const int MaxChatMessageCount = 500;
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int WmHotKey = 0x0312;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint DefaultDpi = 96;
    private const double ResizeBorderWidthDip = 8.0;
    private const double TopCollapsedBarHeight = 20.0;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
    private static readonly TimeSpan UiFadeDuration =
        TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CollapseAnimationDuration =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UpdateCheckInterval =
        TimeSpan.FromHours(24);

    private readonly NetCap _netCap = new();
    private readonly List<ChatMessage> _chatHistory = [];
    private readonly HashSet<int> _reportedUnknownChannelTypes = [];
    private readonly NotificationSoundPlayer _notificationSoundPlayer = new();
    private readonly CancellationTokenSource _updateCheckCancellation = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _windowPlacementSaveTimer;
    private AppConfig _appConfig = new();
    private string[] _chatFilterKeywords = [];
    private string[] _hiddenChatKeywords = [];
    private string[] _mentionKeywords = [];
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private GlobalHotkeyManager? _globalHotkeyManager;
    private IReadOnlyList<HotkeyRegistrationResult>
        _startupHotkeyRegistrationResults = [];
    private bool _clickThroughDisabledForHotkeyFailure;
    private bool _windowPlacementTrackingEnabled;
    private CollapseState _collapseState = CollapseState.Expanded;
    private Rect? _expandedBounds;
    private double _expandedMinWidth;
    private double _expandedMinHeight;
    private string _activeCollapseSide = AppConfig.CollapseSideRight;
    private int _shutdownStarted;

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;

        _windowPlacementSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _windowPlacementSaveTimer.Tick += WindowPlacementSaveTimer_Tick;
        LocationChanged += MainWindow_LocationChanged;
        SizeChanged += MainWindow_SizeChanged;
        ContentRendered += MainWindow_ContentRendered;

        /*
         * NetCapの解析処理はバックグラウンドスレッドで動きます。
         * 受信イベントを画面へ接続します。
         */
        ChatCaptureManager.ChatReceived += OnChatReceived;

        try
        {
            CaptureStatusText.Text = "設定ファイルを読み込んでいます...";

            _appConfig = ConfigManager.Load();

            RestoreWindowPlacement(_appConfig);
            ApplyDisplaySettings(_appConfig);

            var netCapConfig = new NetCapConfig
            {
                ExeNames = _appConfig.ExeNames.ToArray(),
                CaptureDeviceName = _appConfig.CaptureDeviceName
            };

            CaptureStatusText.Text = "Npcapを初期化しています...";

            Log.Information("NetCap initialization started");
            _netCap.Init(netCapConfig);
            Log.Information("NetCap initialization completed");

            ChatCaptureBootstrap.Initialize(_netCap);

            _netCap.Start();

            CaptureStatusText.Text =
                "パケット取得を開始しました。ゲーム内チャットを送信してください。";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Application startup failed");
            CaptureStatusText.Text =
                $"起動中にエラーが発生しました。\n\n{ex}";
        }

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };

        _statusTimer.Tick += UpdateCaptureStatus;
        _statusTimer.Start();
    }

    private async void MainWindow_ContentRendered(
        object? sender,
        EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;

        ShowStartupHotkeyRegistrationFailures();

        try
        {
            await CheckForUpdatesOnStartupAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Unexpected failure during the startup update check");
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_appConfig.CheckForUpdatesOnStartup)
        {
            return;
        }

        DateTime checkStartedUtc = DateTime.UtcNow;
        DateTime? previousSuccessfulCheckUtc =
            _appConfig.LastSuccessfulUpdateCheckUtc;
        bool cooldownElapsed = HasUpdateCheckCooldownElapsed(
            checkStartedUtc,
            previousSuccessfulCheckUtc);
        if (!cooldownElapsed)
        {
            Log.Debug(
                "Skipped startup update check because the 24-hour interval has not elapsed. LastSuccessfulCheckUtc: {LastSuccessfulCheckUtc}",
                previousSuccessfulCheckUtc);
            return;
        }

        CancellationToken updateCheckToken =
            _updateCheckCancellation.Token;
        UpdateCheckResult result = await UpdateCheckService.CheckAsync(
            updateCheckToken);
        if (!result.IsSuccess ||
            updateCheckToken.IsCancellationRequested)
        {
            return;
        }

        _appConfig.LastSuccessfulUpdateCheckUtc = DateTime.UtcNow;

        bool shouldNotify =
            result.IsUpdateAvailable &&
            result.LatestVersionText is { } latestVersionText &&
            ShouldNotifyUpdate(
                latestVersionText,
                _appConfig.LastNotifiedVersion,
                checkStartedUtc,
                previousSuccessfulCheckUtc);

        if (shouldNotify &&
            result.LatestVersionText is { } notifiedVersion)
        {
            _appConfig.LastNotifiedVersion = notifiedVersion;
        }

        SaveConfigSafely(_appConfig);

        if (!shouldNotify ||
            !IsVisible ||
            Volatile.Read(ref _shutdownStarted) != 0 ||
            result.LatestVersionText is not { } latest ||
            result.ReleasePageUri is not { } releasePageUri)
        {
            return;
        }

        var dialog = new UpdateAvailableWindow(
            "新しいバージョンがあります。",
            result.CurrentVersionText,
            latest,
            releasePageUri,
            "あとで")
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private static bool HasUpdateCheckCooldownElapsed(
        DateTime utcNow,
        DateTime? lastSuccessfulCheckUtc)
    {
        if (lastSuccessfulCheckUtc is not { } lastCheck)
        {
            return true;
        }

        DateTime normalizedLastCheck = lastCheck.Kind switch
        {
            DateTimeKind.Utc => lastCheck,
            DateTimeKind.Local => lastCheck.ToUniversalTime(),
            _ => DateTime.SpecifyKind(lastCheck, DateTimeKind.Utc)
        };

        return normalizedLastCheck > utcNow ||
               utcNow - normalizedLastCheck >= UpdateCheckInterval;
    }

    private static bool ShouldNotifyUpdate(
        string latestVersion,
        string lastNotifiedVersion,
        DateTime utcNow,
        DateTime? previousSuccessfulCheckUtc)
    {
        if (!string.Equals(
                latestVersion,
                lastNotifiedVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasUpdateCheckCooldownElapsed(
            utcNow,
            previousSuccessfulCheckUtc);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);

        _globalHotkeyManager = new GlobalHotkeyManager(_windowHandle);
        _startupHotkeyRegistrationResults =
            _globalHotkeyManager.RegisterInitial(_appConfig.Hotkeys);

        bool clickThroughHotkeyFailed =
            _startupHotkeyRegistrationResults.Any(result =>
                result.Action == HotkeyAction.ClickThroughToggle &&
                result.State == HotkeyRegistrationState.Failed);
        if (clickThroughHotkeyFailed && _appConfig.ClickThrough)
        {
            _appConfig.ClickThrough = false;
            _clickThroughDisabledForHotkeyFailure = true;
            SaveConfigSafely(_appConfig);
        }

        ApplyDisplaySettings(_appConfig);
        _windowPlacementTrackingEnabled = true;
    }

    private void ShowStartupHotkeyRegistrationFailures()
    {
        if (!_startupHotkeyRegistrationResults.Any(result =>
                result.State == HotkeyRegistrationState.Failed))
        {
            return;
        }

        var message = new StringBuilder(
            "一部のホットキーを登録できませんでした。\n\n");
        AppendHotkeyRegistrationResults(
            message,
            _startupHotkeyRegistrationResults,
            registrationSucceededText: "登録成功");

        if (_clickThroughDisabledForHotkeyFailure)
        {
            message.AppendLine();
            message.AppendLine(
                "操作不能を防ぐため、クリック透過をOFFに戻しました。");
        }

        message.AppendLine();
        message.Append(
            "他のアプリとの競合がないか確認し、\n" +
            "「システム ＞ ホットキー」から変更してください。");

        MessageBox.Show(
            this,
            message.ToString(),
            "BPSR Chat Overlay",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        _startupHotkeyRegistrationResults = [];
    }

    private void ShowHotkeyUpdateFailure(
        IReadOnlyList<HotkeyRegistrationResult> results)
    {
        var message = new StringBuilder(
            "ホットキーを変更できませんでした。\n\n");
        AppendHotkeyRegistrationResults(
            message,
            results,
            registrationSucceededText: "登録成功（適用は中止）");
        message.AppendLine();
        message.Append(
            "設定と現在有効なホットキーは変更されていません。");

        MessageBox.Show(
            this,
            message.ToString(),
            "BPSR Chat Overlay",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void AppendHotkeyRegistrationResults(
        StringBuilder message,
        IEnumerable<HotkeyRegistrationResult> results,
        string registrationSucceededText)
    {
        foreach (HotkeyRegistrationResult result in results)
        {
            message.Append(
                HotkeyUtilities.GetActionDisplayName(result.Action));
            message.Append("：");

            switch (result.State)
            {
                case HotkeyRegistrationState.Registered:
                    message.Append(registrationSucceededText);
                    break;
                case HotkeyRegistrationState.NotConfigured:
                    message.Append("未設定");
                    break;
                case HotkeyRegistrationState.Failed:
                    if (result.Gesture is { } gesture)
                    {
                        message.Append(HotkeyUtilities.FormatGesture(gesture));
                        message.Append(" — ");
                    }

                    message.Append("登録失敗");
                    message.Append($"（Win32エラー: {result.ErrorCode}）");
                    break;
            }

            message.AppendLine();
        }
    }

    private void ApplyDisplaySettings(AppConfig config)
    {
        _chatFilterKeywords = ParseKeywords(config.ChatFilterKeywords);
        _hiddenChatKeywords = ParseKeywords(config.HiddenChatKeywords);
        _mentionKeywords = ParseKeywords(config.MentionKeywords);
        ChatColors.Apply(
            config.WorldChatTextColor,
            config.ChannelChatTextColor,
            config.PartyChatTextColor,
            config.GuildChatTextColor,
            config.NewbieChatTextColor,
            config.TalkChatTextColor,
            config.ChatBackgroundColor,
            config.MenuBackgroundColor,
            config.MentionHighlightColor,
            config.TalkHighlightBackgroundColor);
        ChatListBox.FontFamily = ChatFontCatalog.Resolve(
            config.ChatFontFamily);
        ChatListBox.FontSize = Math.Clamp(config.FontSize, 8, 48);
        Resources["TimeColumnWidth"] = new GridLength(Math.Clamp(
            config.TimeColumnWidth,
            AppConfig.MinTimeColumnWidth,
            AppConfig.MaxTimeColumnWidth));
        Resources["SenderNameColumnWidth"] = new GridLength(Math.Clamp(
            config.SenderNameColumnWidth,
            AppConfig.MinSenderNameColumnWidth,
            AppConfig.MaxSenderNameColumnWidth));
        string colorBandPosition =
            AppConfig.NormalizeChatColorBandPosition(
                config.ChatColorBandPosition);
        double colorBandWidth = (double)Resources["ChatColorBandWidth"];
        Resources["LeftChatColorBandWidth"] = new GridLength(
            config.ShowChatColorBand &&
            colorBandPosition == AppConfig.ChatColorBandPositionLeft
                ? colorBandWidth
                : 0);
        Resources["RightChatColorBandWidth"] = new GridLength(
            config.ShowChatColorBand &&
            colorBandPosition == AppConfig.ChatColorBandPositionRight
                ? colorBandWidth
                : 0);
        ChatListBox.Tag = config;
        Resources["ChatZebraStripeBrush"] = config.ShowChatZebraStripes
            ? Resources["ChatZebraStripeEnabledBrush"]
            : Brushes.Transparent;
        ChatBackgroundBorder.Background = ChatColors.ChatBackground;
        Resources["ChatBackgroundOpacity"] =
            Math.Clamp(config.BackgroundOpacity, 0.0, 1.0);
        Resources["WindowDecorationOpacity"] =
            Math.Clamp(config.MenuBackgroundOpacity, 0.0, 1.0);
        Resources["ChatTextOpacity"] =
            Math.Clamp(config.TextOpacity, 0.0, 1.0);
        Resources["ChatTextShadowColor"] = ChatColors.CreateBrush(
            config.ChatTextShadowColor,
            ChatColors.DefaultChatTextShadowColor).Color;
        MenuBackgroundBorder.Background = ChatColors.MenuBackground;
        CollapsedMenuBackgroundBorder.Background = ChatColors.MenuBackground;
        UpdateEdgeHandleBrushes(config.EdgeHandleOpacity);
        DebugPanel.Visibility = config.ShowDebugPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatTogglePanel.Visibility = config.ShowChatToggleButtons
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatFilterToggleButton.Visibility = config.ShowChatFilterToggle
            ? Visibility.Visible
            : Visibility.Collapsed;
        MentionHighlightToggleButton.Visibility =
            config.ShowMentionHighlightToggle
                ? Visibility.Visible
                : Visibility.Collapsed;
        FeatureTogglePanel.Visibility =
            config.ShowChatFilterToggle ||
            config.ShowMentionHighlightToggle
                ? Visibility.Visible
                : Visibility.Collapsed;
        UpdateChatToggleButtonStates();
        UpdateFeatureToggleButtonStates();
        UpdateCollapseButtonAppearance(config.CollapseSide);
        Topmost = config.TopMost;

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyClickThrough(config.ClickThrough);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(
            _appConfig,
            _netCap.CaptureDeviceSelection?.ActualDeviceName)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true &&
            settingsWindow.SavedConfig is { } savedConfig)
        {
            if (_globalHotkeyManager is null)
            {
                MessageBox.Show(
                    this,
                    "ホットキー管理を初期化できていないため、設定を保存できません。",
                    "BPSR Chat Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            HotkeyUpdatePreparation preparation =
                _globalHotkeyManager.PrepareUpdate(savedConfig.Hotkeys);
            if (!preparation.IsSuccess ||
                preparation.PreparedUpdate is not { } preparedUpdate)
            {
                ShowHotkeyUpdateFailure(preparation.Results);
                return;
            }

            using (preparedUpdate)
            {
                if (!SaveConfigSafely(savedConfig))
                {
                    preparedUpdate.Rollback();
                    MessageBox.Show(
                        this,
                        "設定を保存できませんでした。ログを確認してください。\n\n" +
                        "設定と現在有効なホットキーは変更されていません。",
                        "BPSR Chat Overlay",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                preparedUpdate.Commit();
            }

            _appConfig = savedConfig;
            ApplyDisplaySettings(_appConfig);
            ReevaluateMentionStatus();
            RebuildDisplayedChatMessages();
        }
    }

    private void ChatToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton
            {
                Tag: string chatType
            } toggleButton)
        {
            return;
        }

        bool isEnabled = toggleButton.IsChecked == true;

        switch (chatType)
        {
            case "World":
                _appConfig.ShowWorldChat = isEnabled;
                break;
            case "Guild":
                _appConfig.ShowGuildChat = isEnabled;
                break;
            case "Party":
                _appConfig.ShowPartyChat = isEnabled;
                break;
            case "Channel":
                _appConfig.ShowChannelChat = isEnabled;
                break;
            case "Newbie":
                _appConfig.ShowNewbieChat = isEnabled;
                break;
            case "Talk":
                _appConfig.ShowTalkChat = isEnabled;
                break;
            default:
                return;
        }

        RebuildDisplayedChatMessages();
        SaveConfigSafely(_appConfig);
    }

    private void FeatureToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not ToggleButton
            {
                Tag: string featureType
            } toggleButton)
        {
            return;
        }

        bool isEnabled = toggleButton.IsChecked == true;

        switch (featureType)
        {
            case "ChatFilter":
                SetChatFilterEnabled(isEnabled);
                break;
            case "MentionHighlight":
                SetMentionHighlightEnabled(isEnabled);
                break;
        }
    }

    private void SetChatFilterEnabled(bool enabled)
    {
        _appConfig.EnableChatFilter = enabled;
        UpdateFeatureToggleButtonStates();
        RebuildDisplayedChatMessages();
        SaveConfigSafely(_appConfig);
    }

    private void SetMentionHighlightEnabled(bool enabled)
    {
        _appConfig.EnableMentionNotification = enabled;
        UpdateFeatureToggleButtonStates();
        ReevaluateMentionStatus();
        RebuildDisplayedChatMessages();
        SaveConfigSafely(_appConfig);
    }

    private void UpdateFeatureToggleButtonStates()
    {
        ChatFilterToggleButton.IsChecked =
            _appConfig.EnableChatFilter;
        ChatFilterToggleButton.ToolTip = CreateFeatureToggleToolTip(
            "キーワードフィルター",
            _appConfig.EnableChatFilter);

        MentionHighlightToggleButton.IsChecked =
            _appConfig.EnableMentionNotification;
        MentionHighlightToggleButton.ToolTip = CreateFeatureToggleToolTip(
            "キーワードハイライト",
            _appConfig.EnableMentionNotification);
    }

    private static string CreateFeatureToggleToolTip(
        string featureName,
        bool enabled)
    {
        string currentState = enabled ? "ON" : "OFF";
        string nextState = enabled ? "OFF" : "ON";

        return $"{featureName}：{currentState}\n" +
               $"クリックして{nextState}にします";
    }

    private void UpdateCollapseButtonAppearance(string? configuredSide)
    {
        string side = AppConfig.NormalizeCollapseSide(configuredSide);
        bool useEdgeHandle = side != AppConfig.CollapseSideTop;
        bool showCollapseControl = _appConfig.ShowCollapseButton;
        double edgeHandleThickness = GetEdgeHandleThickness();

        CollapseButton.Content = "▲";
        CollapseButton.ToolTip = "上側へ収納します";
        CollapseButton.Visibility =
            showCollapseControl && !useEdgeHandle
                ? Visibility.Visible
                : Visibility.Collapsed;

        EdgeCollapseButton.Visibility =
            showCollapseControl && useEdgeHandle
                ? Visibility.Visible
                : Visibility.Collapsed;
        ConfigureEdgeCollapseButton(side);

        NormalUiRoot.Margin =
            showCollapseControl && useEdgeHandle
                ? side switch
                {
                    AppConfig.CollapseSideLeft =>
                        new Thickness(edgeHandleThickness, 0, 0, 0),
                    AppConfig.CollapseSideBottom =>
                        new Thickness(0, 0, 0, edgeHandleThickness),
                    _ => new Thickness(0, 0, edgeHandleThickness, 0)
                }
                : new Thickness(0);
    }

    private void ConfigureEdgeCollapseButton(string side)
    {
        bool isCollapsed =
            _collapseState is CollapseState.Collapsed or
                CollapseState.Expanding;
        double edgeHandleThickness = GetEdgeHandleThickness();

        EdgeCollapseButton.HorizontalAlignment = side switch
        {
            AppConfig.CollapseSideLeft => HorizontalAlignment.Left,
            AppConfig.CollapseSideBottom => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Right
        };
        EdgeCollapseButton.VerticalAlignment = side switch
        {
            AppConfig.CollapseSideBottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Stretch
        };
        EdgeCollapseButton.Width =
            side == AppConfig.CollapseSideBottom
                ? double.NaN
                : edgeHandleThickness;
        EdgeCollapseButton.Height =
            side == AppConfig.CollapseSideBottom
                ? edgeHandleThickness
                : double.NaN;

        EdgeCollapseButton.Content = side switch
        {
            AppConfig.CollapseSideLeft => isCollapsed ? "▶" : "◀",
            AppConfig.CollapseSideBottom => isCollapsed ? "▲" : "▼",
            _ => isCollapsed ? "◀" : "▶"
        };
        EdgeCollapseButton.ToolTip = isCollapsed
            ? "Overlayを展開します"
            : side switch
        {
            AppConfig.CollapseSideLeft => "左側へ収納します",
            AppConfig.CollapseSideBottom => "下側へ収納します",
            _ => "右側へ収納します"
        };
    }

    private double GetEdgeHandleThickness()
    {
        return Math.Clamp(
            _appConfig.EdgeHandleThickness,
            AppConfig.MinEdgeHandleThickness,
            AppConfig.MaxEdgeHandleThickness);
    }

    private double GetCollapsedThickness(string side)
    {
        return side == AppConfig.CollapseSideTop
            ? TopCollapsedBarHeight
            : GetEdgeHandleThickness();
    }

    private void UpdateEdgeHandleBrushes(double configuredOpacity)
    {
        double opacity = Math.Clamp(configuredOpacity, 0.0, 1.0);
        double hoverOpacity = Math.Min(1.0, opacity + 0.12);

        Resources["CollapsedHandleBackgroundBrush"] =
            CreateBrush(0x18, 0x1E, 0x27, opacity);
        Resources["CollapsedHandleBorderBrush"] =
            CreateBrush(0x6E, 0x7B, 0x8B, opacity);
        Resources["CollapsedHandleHoverBackgroundBrush"] =
            CreateBrush(0x2A, 0x33, 0x3F, hoverOpacity);
        Resources["CollapsedHandleHoverBorderBrush"] =
            CreateBrush(0x8E, 0x9C, 0xAC, hoverOpacity);
    }

    private static SolidColorBrush CreateBrush(
        byte red,
        byte green,
        byte blue,
        double opacity)
    {
        byte alpha = (byte)Math.Round(
            Math.Clamp(opacity, 0.0, 1.0) * byte.MaxValue);
        return new SolidColorBrush(
            Color.FromArgb(alpha, red, green, blue));
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCollapse();
    }

    private void ToggleCollapse()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        switch (_collapseState)
        {
            case CollapseState.Expanded:
                _ = CollapseWindowAsync();
                break;
            case CollapseState.Collapsed:
                _ = ExpandWindowAsync();
                break;
        }
    }

    private async Task CollapseWindowAsync()
    {
        Rect expandedBounds = new(
            Left,
            Top,
            ActualWidth,
            ActualHeight);

        if (!IsValidWindowBounds(expandedBounds))
        {
            return;
        }

        SaveWindowPlacement();
        _windowPlacementSaveTimer.Stop();
        _expandedBounds = expandedBounds;
        _expandedMinWidth = MinWidth;
        _expandedMinHeight = MinHeight;
        _activeCollapseSide =
            AppConfig.NormalizeCollapseSide(_appConfig.CollapseSide);
        _collapseState = CollapseState.Collapsing;

        try
        {
            await AnimateOpacityAsync(
                NormalUiRoot,
                0,
                UiFadeDuration);
            NormalUiRoot.Visibility = Visibility.Collapsed;

            ConfigureCollapsedMinimumSize(_activeCollapseSide);
            ConfigureExpandButton(_activeCollapseSide);
            ExpandButton.Visibility = Visibility.Collapsed;
            TopCollapsedBar.Visibility = Visibility.Collapsed;
            CollapseAnimationFrame.Visibility = Visibility.Visible;
            CollapsedUiRoot.Visibility = Visibility.Visible;

            double collapsedThickness =
                GetCollapsedThickness(_activeCollapseSide);
            Rect collapsedBounds = CalculateCollapsedBounds(
                expandedBounds,
                _activeCollapseSide,
                collapsedThickness);
            collapsedBounds = EnsureCollapsedButtonVisible(
                collapsedBounds,
                _activeCollapseSide);

            await AnimateWindowBoundsAsync(collapsedBounds);

            CollapseAnimationFrame.Visibility = Visibility.Collapsed;
            _collapseState = CollapseState.Collapsed;
            if (_activeCollapseSide == AppConfig.CollapseSideTop)
            {
                TopCollapsedBar.Visibility = Visibility.Visible;
                ExpandButton.Visibility = _appConfig.ShowCollapseButton
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            UpdateCollapseButtonAppearance(_activeCollapseSide);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(ex, "Failed to collapse the overlay window");
            RestoreExpandedStateAfterAnimationFailure();
        }
    }

    private async Task ExpandWindowAsync()
    {
        if (_expandedBounds is not { } expandedBounds ||
            !IsValidWindowBounds(expandedBounds))
        {
            RestoreExpandedStateAfterAnimationFailure();
            return;
        }

        _collapseState = CollapseState.Expanding;

        try
        {
            ExpandButton.Visibility = Visibility.Collapsed;
            TopCollapsedBar.Visibility = Visibility.Collapsed;
            CollapseAnimationFrame.Visibility = Visibility.Visible;

            await AnimateWindowBoundsAsync(expandedBounds);

            MinWidth = _expandedMinWidth;
            MinHeight = _expandedMinHeight;
            CollapseAnimationFrame.Visibility = Visibility.Collapsed;
            CollapsedUiRoot.Visibility = Visibility.Collapsed;
            NormalUiRoot.Opacity = 0;
            NormalUiRoot.Visibility = Visibility.Visible;

            await AnimateOpacityAsync(
                NormalUiRoot,
                1,
                UiFadeDuration);

            _collapseState = CollapseState.Expanded;
            _expandedBounds = null;
            UpdateCollapseButtonAppearance(_appConfig.CollapseSide);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(ex, "Failed to expand the overlay window");
            RestoreExpandedStateAfterAnimationFailure();
        }
    }

    private void ConfigureCollapsedMinimumSize(string side)
    {
        double collapsedThickness = GetCollapsedThickness(side);

        if (side is AppConfig.CollapseSideLeft or
            AppConfig.CollapseSideRight)
        {
            MinWidth = collapsedThickness;
        }
        else
        {
            MinHeight = collapsedThickness;
        }
    }

    private void ConfigureExpandButton(string side)
    {
        ExpandButton.Content = side switch
        {
            AppConfig.CollapseSideLeft => "▶",
            AppConfig.CollapseSideTop => "▼",
            AppConfig.CollapseSideBottom => "▲",
            _ => "◀"
        };
    }

    private static Rect CalculateCollapsedBounds(
        Rect expandedBounds,
        string side,
        double collapsedThickness)
    {
        return side switch
        {
            AppConfig.CollapseSideLeft => new Rect(
                expandedBounds.Left,
                expandedBounds.Top,
                collapsedThickness,
                expandedBounds.Height),
            AppConfig.CollapseSideTop => new Rect(
                expandedBounds.Left,
                expandedBounds.Top,
                expandedBounds.Width,
                collapsedThickness),
            AppConfig.CollapseSideBottom => new Rect(
                expandedBounds.Left,
                expandedBounds.Bottom - collapsedThickness,
                expandedBounds.Width,
                collapsedThickness),
            _ => new Rect(
                expandedBounds.Right - collapsedThickness,
                expandedBounds.Top,
                collapsedThickness,
                expandedBounds.Height)
        };
    }

    private Rect EnsureCollapsedButtonVisible(
        Rect collapsedBounds,
        string side)
    {
        Rect workingArea = GetCurrentWorkingAreaInDips();
        if (workingArea.IsEmpty)
        {
            return collapsedBounds;
        }

        double left = collapsedBounds.Left;
        double top = collapsedBounds.Top;
        double collapsedThickness = GetCollapsedThickness(side);

        if (side is AppConfig.CollapseSideLeft or
            AppConfig.CollapseSideRight)
        {
            left = Math.Clamp(
                left,
                workingArea.Left,
                workingArea.Right - collapsedBounds.Width);
            double buttonCenter = Math.Clamp(
                collapsedBounds.Top + (collapsedBounds.Height / 2),
                workingArea.Top + (collapsedThickness / 2),
                workingArea.Bottom - (collapsedThickness / 2));
            top += buttonCenter -
                   (collapsedBounds.Top + (collapsedBounds.Height / 2));
        }
        else
        {
            top = Math.Clamp(
                top,
                workingArea.Top,
                workingArea.Bottom - collapsedBounds.Height);
            double buttonCenter = Math.Clamp(
                collapsedBounds.Left + (collapsedBounds.Width / 2),
                workingArea.Left + (collapsedThickness / 2),
                workingArea.Right - (collapsedThickness / 2));
            left += buttonCenter -
                    (collapsedBounds.Left + (collapsedBounds.Width / 2));
        }

        return new Rect(
            left,
            top,
            collapsedBounds.Width,
            collapsedBounds.Height);
    }

    private Rect GetCurrentWorkingAreaInDips()
    {
        var area =
            System.Windows.Forms.Screen.FromHandle(_windowHandle).WorkingArea;
        GetMonitorDpi(area, out uint dpiX, out uint dpiY);

        return new Rect(
            area.Left * DefaultDpi / dpiX,
            area.Top * DefaultDpi / dpiY,
            area.Width * DefaultDpi / dpiX,
            area.Height * DefaultDpi / dpiY);
    }

    private Task AnimateWindowBoundsAsync(Rect targetBounds)
    {
        double startWidth = ActualWidth;
        double startHeight = ActualHeight;
        var storyboard = new Storyboard
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        var easing = new CubicEase
        {
            EasingMode = EasingMode.EaseInOut
        };

        AddWindowAnimation(
            storyboard,
            LeftProperty,
            Left,
            targetBounds.Left,
            easing);
        AddWindowAnimation(
            storyboard,
            TopProperty,
            Top,
            targetBounds.Top,
            easing);
        AddWindowAnimation(
            storyboard,
            WidthProperty,
            startWidth,
            targetBounds.Width,
            easing);
        AddWindowAnimation(
            storyboard,
            HeightProperty,
            startHeight,
            targetBounds.Height,
            easing);

        var completion = new TaskCompletionSource();
        storyboard.Completed += (_, _) =>
        {
            storyboard.Remove(this);
            Left = targetBounds.Left;
            Top = targetBounds.Top;
            Width = targetBounds.Width;
            Height = targetBounds.Height;
            completion.TrySetResult();
        };
        storyboard.Begin(
            this,
            HandoffBehavior.SnapshotAndReplace,
            isControllable: true);

        return completion.Task;
    }

    private void AddWindowAnimation(
        Storyboard storyboard,
        DependencyProperty property,
        double from,
        double to,
        IEasingFunction easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = CollapseAnimationDuration,
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(
            animation,
            new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private static Task AnimateOpacityAsync(
        UIElement element,
        double targetOpacity,
        TimeSpan duration)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            From = element.Opacity,
            To = targetOpacity,
            Duration = duration,
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            }
        };
        animation.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = targetOpacity;
            completion.TrySetResult();
        };
        element.BeginAnimation(
            UIElement.OpacityProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);

        return completion.Task;
    }

    private void RestoreExpandedStateAfterAnimationFailure()
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);

        if (_expandedBounds is { } expandedBounds &&
            IsValidWindowBounds(expandedBounds))
        {
            Left = expandedBounds.Left;
            Top = expandedBounds.Top;
            Width = expandedBounds.Width;
            Height = expandedBounds.Height;
        }

        MinWidth = _expandedMinWidth > 0
            ? _expandedMinWidth
            : MinWidth;
        MinHeight = _expandedMinHeight > 0
            ? _expandedMinHeight
            : MinHeight;
        CollapsedUiRoot.Visibility = Visibility.Collapsed;
        TopCollapsedBar.Visibility = Visibility.Collapsed;
        CollapseAnimationFrame.Visibility = Visibility.Collapsed;
        ExpandButton.Visibility = Visibility.Collapsed;
        NormalUiRoot.BeginAnimation(UIElement.OpacityProperty, null);
        NormalUiRoot.Opacity = 1;
        NormalUiRoot.Visibility = Visibility.Visible;
        _collapseState = CollapseState.Expanded;
        _expandedBounds = null;
        UpdateCollapseButtonAppearance(_appConfig.CollapseSide);
    }

    private void UpdateChatToggleButtonStates()
    {
        foreach (ToggleButton toggleButton in
                 ChatTogglePanel.Children.OfType<ToggleButton>())
        {
            toggleButton.IsChecked = toggleButton.Tag switch
            {
                "World" => _appConfig.ShowWorldChat,
                "Guild" => _appConfig.ShowGuildChat,
                "Party" => _appConfig.ShowPartyChat,
                "Channel" => _appConfig.ShowChannelChat,
                "Newbie" => _appConfig.ShowNewbieChat,
                "Talk" => _appConfig.ShowTalkChat,
                _ => false
            };
        }
    }

    private void TitleBarRoot_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_collapseState != CollapseState.Expanded ||
            e.ChangedButton != MouseButton.Left ||
            IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // マウスボタンが既に離された場合は移動を中止します。
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement contentElement =>
                    contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return false;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyClickThrough(bool enabled)
    {
        IntPtr currentStyle = GetWindowLongPtr(_windowHandle, GwlExStyle);
        long updatedStyle = currentStyle.ToInt64();

        if (enabled)
        {
            updatedStyle |= WsExTransparent | WsExLayered;
        }
        else
        {
            updatedStyle &= ~WsExTransparent;
        }

        SetWindowLongPtr(
            _windowHandle,
            GwlExStyle,
            new IntPtr(updatedStyle));

        Debug.WriteLine(
            enabled
                ? "ClickThrough enabled"
                : "ClickThrough disabled");
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey &&
            _globalHotkeyManager?.TryGetAction(
                wParam.ToInt32(),
                out HotkeyAction hotkeyAction) == true)
        {
            if (hotkeyAction == HotkeyAction.ClickThroughToggle)
            {
                _appConfig.ClickThrough = !_appConfig.ClickThrough;
                ApplyClickThrough(_appConfig.ClickThrough);
                SaveConfigSafely(_appConfig);
            }
            else if (hotkeyAction == HotkeyAction.CollapseToggle)
            {
                ToggleCollapse();
            }

            handled = true;
        }
        else if (message == WmNcHitTest &&
                 !_appConfig.ClickThrough &&
                 _collapseState == CollapseState.Expanded)
        {
            int hitTest = GetResizeHitTest(lParam);

            if (hitTest != 0)
            {
                handled = true;
                return new IntPtr(hitTest);
            }
        }

        return IntPtr.Zero;
    }

    private int GetResizeHitTest(IntPtr lParam)
    {
        if (!GetWindowRect(_windowHandle, out NativeRect windowRect))
        {
            return 0;
        }

        long screenPosition = lParam.ToInt64();
        int x = unchecked((short)screenPosition);
        int y = unchecked((short)(screenPosition >> 16));
        int resizeBorderPixels = (int)Math.Ceiling(
            ResizeBorderWidthDip * GetWindowDpi() / DefaultDpi);

        bool isLeft = x >= windowRect.Left &&
                      x < windowRect.Left + resizeBorderPixels;
        bool isRight = x < windowRect.Right &&
                       x >= windowRect.Right - resizeBorderPixels;
        bool isTop = y >= windowRect.Top &&
                     y < windowRect.Top + resizeBorderPixels;
        bool isBottom = y < windowRect.Bottom &&
                        y >= windowRect.Bottom - resizeBorderPixels;

        if (isLeft && isTop)
        {
            return HtTopLeft;
        }

        if (isRight && isTop)
        {
            return HtTopRight;
        }

        if (isLeft && isBottom)
        {
            return HtBottomLeft;
        }

        if (isRight && isBottom)
        {
            return HtBottomRight;
        }

        if (isLeft)
        {
            return HtLeft;
        }

        if (isRight)
        {
            return HtRight;
        }

        return isBottom ? HtBottom : 0;
    }

    private uint GetWindowDpi()
    {
        try
        {
            uint dpi = GetDpiForWindow(_windowHandle);
            return dpi == 0 ? DefaultDpi : dpi;
        }
        catch (EntryPointNotFoundException)
        {
            return DefaultDpi;
        }
    }

    private static bool SaveConfigSafely(AppConfig config)
    {
        try
        {
            ConfigManager.Save(config);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save config.json: {ex}");
            return false;
        }
    }

    private void RestoreWindowPlacement(AppConfig config)
    {
        if (IsValidWindowSize(config.WindowWidth, config.WindowHeight))
        {
            Width = config.WindowWidth;
            Height = config.WindowHeight;
        }

        if (config.WindowLeft is not { } left ||
            config.WindowTop is not { } top ||
            !double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !IsWindowAreaVisible(left, top, Width, Height))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
    }

    private bool IsValidWindowSize(double width, double height)
    {
        return double.IsFinite(width) &&
               double.IsFinite(height) &&
               width > 0 &&
               height > 0 &&
               width >= MinWidth &&
               height >= MinHeight;
    }

    private bool IsValidWindowBounds(Rect bounds)
    {
        return double.IsFinite(bounds.Left) &&
               double.IsFinite(bounds.Top) &&
               IsValidWindowSize(bounds.Width, bounds.Height);
    }

    private static bool IsWindowAreaVisible(
        double left,
        double top,
        double width,
        double height)
    {
        double right = left + width;
        double bottom = top + height;

        if (!double.IsFinite(right) || !double.IsFinite(bottom))
        {
            return false;
        }

        return System.Windows.Forms.Screen.AllScreens.Any(screen =>
        {
            var area = screen.WorkingArea;
            GetMonitorDpi(area, out uint dpiX, out uint dpiY);

            double areaLeft = area.Left * DefaultDpi / dpiX;
            double areaTop = area.Top * DefaultDpi / dpiY;
            double areaWidth = area.Width * DefaultDpi / dpiX;
            double areaHeight = area.Height * DefaultDpi / dpiY;
            double areaRight = areaLeft + areaWidth;
            double areaBottom = areaTop + areaHeight;

            return left < areaRight &&
                   right > areaLeft &&
                   top < areaBottom &&
                   bottom > areaTop;
        });
    }

    private static void GetMonitorDpi(
        System.Drawing.Rectangle workingArea,
        out uint dpiX,
        out uint dpiY)
    {
        dpiX = DefaultDpi;
        dpiY = DefaultDpi;

        try
        {
            var monitorPoint = new NativePoint
            {
                X = workingArea.Left + (workingArea.Width / 2),
                Y = workingArea.Top + (workingArea.Height / 2)
            };
            IntPtr monitor = MonitorFromPoint(
                monitorPoint,
                MonitorDefaultToNearest);

            if (monitor == IntPtr.Zero ||
                GetDpiForMonitor(
                    monitor,
                    MonitorDpiType.Effective,
                    out uint monitorDpiX,
                    out uint monitorDpiY) != 0 ||
                monitorDpiX == 0 ||
                monitorDpiY == 0)
            {
                return;
            }

            dpiX = monitorDpiX;
            dpiY = monitorDpiY;
        }
        catch (DllNotFoundException)
        {
            // DPI取得APIを利用できない環境では96 DPIとして判定します。
        }
        catch (EntryPointNotFoundException)
        {
            // DPI取得APIを利用できない環境では96 DPIとして判定します。
        }
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        ScheduleWindowPlacementSave();
    }

    private void MainWindow_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ScheduleWindowPlacementSave();
    }

    private void ScheduleWindowPlacementSave()
    {
        if (!_windowPlacementTrackingEnabled ||
            WindowState == WindowState.Minimized ||
            _collapseState != CollapseState.Expanded)
        {
            return;
        }

        _windowPlacementSaveTimer.Stop();
        _windowPlacementSaveTimer.Start();
    }

    private void WindowPlacementSaveTimer_Tick(
        object? sender,
        EventArgs e)
    {
        _windowPlacementSaveTimer.Stop();
        SaveWindowPlacement();
    }

    private void SaveWindowPlacement()
    {
        if (WindowState == WindowState.Minimized ||
            _collapseState != CollapseState.Expanded)
        {
            return;
        }

        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        SaveWindowPlacement(bounds);
    }

    private void SaveWindowPlacement(Rect bounds)
    {
        if (!IsValidWindowBounds(bounds))
        {
            return;
        }

        _appConfig.WindowLeft = bounds.Left;
        _appConfig.WindowTop = bounds.Top;
        _appConfig.WindowWidth = bounds.Width;
        _appConfig.WindowHeight = bounds.Height;

        SaveConfigSafely(_appConfig);
    }

    private void SaveWindowPlacementForShutdown()
    {
        if (_collapseState == CollapseState.Expanded)
        {
            SaveWindowPlacement();
            return;
        }

        if (_expandedBounds is { } expandedBounds)
        {
            SaveWindowPlacement(expandedBounds);
        }
    }

    private void OnChatReceived(ChatMessage message)
    {
        if (message.ChannelType ==
            (int)Zproto.ChitChatChannelType.ChannelPrivate)
        {
            ProcessTalkMessage(message);
            return;
        }

        ProcessNormalChatMessage(message);
    }

    private void ProcessTalkMessage(ChatMessage message)
    {
        QueueChatMessage(message, isTalk: true);
    }

    private void ProcessNormalChatMessage(ChatMessage message)
    {
        QueueChatMessage(message, isTalk: false);
    }

    private void QueueChatMessage(ChatMessage message, bool isTalk)
    {
        /*
         * ChatReceivedは画面とは別のスレッドから呼ばれる可能性があります。
         * WPFの画面更新はUIスレッドで行う必要があるため、
         * Dispatcherを経由します。
         */
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    LogUnknownChannelType(message.ChannelType);
                    if (isTalk)
                    {
                        message.IsMention = false;
                        message.IsTalkHighlighted =
                            _appConfig.EnableTalkHighlight;

                        if (_appConfig.EnableTalkSound)
                        {
                            _notificationSoundPlayer.Play(
                                _appConfig.TalkSoundFilePath);
                        }
                    }
                    else
                    {
                        message.IsMention = IsMentionMessage(message);

                        if (message.IsMention &&
                            _appConfig.EnableMentionSound)
                        {
                            _notificationSoundPlayer.Play(
                                _appConfig.MentionSoundFilePath);
                        }
                    }

                    AddToChatHistory(message);

                    bool shouldDisplay = isTalk
                        ? _appConfig.ShowTalkChat
                        : ShouldDisplayChatMessage(message);

                    if (shouldDisplay)
                    {
                        ChatMessages.Add(message);
                    }

                    ChatCountText.Text =
                        $"受信件数: {_chatHistory.Count:N0}";

                    if (shouldDisplay)
                    {
                        ChatListBox.ScrollIntoView(message);
                        if (isTalk && message.IsTalkHighlighted)
                        {
                            BeginTalkHighlight(message);
                        }
                        else if (!isTalk)
                        {
                            BeginNewChatHighlight(message);
                        }
                    }
                    else if (isTalk && message.IsTalkHighlighted)
                    {
                        // 非表示中も履歴上の点灯状態は保持します。
                        message.IsTalkHighlightVisible = true;
                    }
                }
                catch (Exception ex) when (IsRecoverableException(ex))
                {
                    Log.Error(
                        ex,
                        "Failed to apply a chat message to the UI. ChannelType: {ChannelType}, HasSenderName: {HasSenderName}",
                        message.ChannelType,
                        !string.IsNullOrEmpty(message.SenderName));
                }
            });
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(
                ex,
                "Failed to queue a chat message for UI processing. ChannelType: {ChannelType}, HasSenderName: {HasSenderName}",
                message.ChannelType,
                !string.IsNullOrEmpty(message.SenderName));
        }
    }

    private void AddToChatHistory(ChatMessage message)
    {
        _chatHistory.Add(message);

        while (_chatHistory.Count > MaxChatMessageCount)
        {
            ChatMessage oldestMessage = _chatHistory[0];
            _chatHistory.RemoveAt(0);
            ChatMessages.Remove(oldestMessage);
        }
    }

    private async void BeginTalkHighlight(ChatMessage message)
    {
        try
        {
            const int blinkCount = 3;
            TimeSpan interval = TimeSpan.FromMilliseconds(400);

            for (int index = 0; index < blinkCount; index++)
            {
                message.IsTalkHighlightVisible = true;
                await Task.Delay(interval);
                message.IsTalkHighlightVisible = false;
                await Task.Delay(interval);
            }

            message.IsTalkHighlightVisible = true;
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(ex, "Failed to animate talk highlighting");
            message.IsTalkHighlightVisible = true;
        }
    }

    private static bool IsRecoverableException(Exception exception)
    {
        return exception is not (
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException);
    }

    private void BeginNewChatHighlight(ChatMessage message)
    {
        if (!_appConfig.HighlightNewChatRows)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                try
                {
                    if (ChatListBox.ItemContainerGenerator
                            .ContainerFromItem(message)
                            is not ListBoxItem item ||
                        item.Template.FindName("NewChatHighlight", item)
                            is not Border highlightBorder ||
                        Resources["NewChatHighlightStoryboard"]
                            is not Storyboard storyboard)
                    {
                        return;
                    }

                    storyboard.Begin(highlightBorder);
                }
                catch (Exception ex) when (IsRecoverableException(ex))
                {
                    Log.Error(
                        ex,
                        "Failed to highlight a chat message. ChannelType: {ChannelType}, HasSenderName: {HasSenderName}",
                        message.ChannelType,
                        !string.IsNullOrEmpty(message.SenderName));
                }
            });
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(
                ex,
                "Failed to queue chat highlighting. ChannelType: {ChannelType}, HasSenderName: {HasSenderName}",
                message.ChannelType,
                !string.IsNullOrEmpty(message.SenderName));
        }
    }

    private bool ShouldDisplayChatMessage(ChatMessage message)
    {
        bool matchesChannelFilter = message.ChannelType switch
        {
            (int)Zproto.ChitChatChannelType.ChannelWorld =>
                _appConfig.ShowWorldChat,
            (int)Zproto.ChitChatChannelType.ChannelScene =>
                _appConfig.ShowChannelChat,
            (int)Zproto.ChitChatChannelType.ChannelTeam =>
                _appConfig.ShowPartyChat,
            (int)Zproto.ChitChatChannelType.ChannelPrivate =>
                _appConfig.ShowTalkChat,
            (int)Zproto.ChitChatChannelType.ChannelUnion =>
                _appConfig.ShowGuildChat,
            (int)Zproto.ChitChatChannelType.ChannelNewbie =>
                _appConfig.ShowNewbieChat,
            _ => true
        };

        if (message.ChannelType ==
            (int)Zproto.ChitChatChannelType.ChannelPrivate)
        {
            return matchesChannelFilter;
        }

        return matchesChannelFilter &&
               !MatchesHiddenChatKeywords(message) &&
               MatchesKeywordFilter(message);
    }

    private void LogUnknownChannelType(int channelType)
    {
        if (IsKnownChatChannelType(channelType) ||
            !_reportedUnknownChannelTypes.Add(channelType))
        {
            return;
        }

        Log.Warning(
            "Unknown chat channel type received: {ChannelType}",
            channelType);
    }

    private static bool IsKnownChatChannelType(int channelType)
    {
        return channelType is
            (int)Zproto.ChitChatChannelType.ChannelWorld or
            (int)Zproto.ChitChatChannelType.ChannelScene or
            (int)Zproto.ChitChatChannelType.ChannelTeam or
            (int)Zproto.ChitChatChannelType.ChannelUnion or
            (int)Zproto.ChitChatChannelType.ChannelPrivate or
            (int)Zproto.ChitChatChannelType.ChannelNewbie;
    }

    private bool MatchesKeywordFilter(ChatMessage message)
    {
        if (!_appConfig.EnableChatFilter ||
            _chatFilterKeywords.Length == 0)
        {
            return true;
        }

        string senderName = message.SenderName ?? string.Empty;
        string messageText = message.Message ?? string.Empty;

        return _chatFilterKeywords.Any(keyword =>
            senderName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            messageText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesHiddenChatKeywords(ChatMessage message)
    {
        if (message.ChannelType ==
                (int)Zproto.ChitChatChannelType.ChannelPrivate ||
            _hiddenChatKeywords.Length == 0)
        {
            return false;
        }

        string messageText = message.Message ?? string.Empty;
        string senderName = message.SenderName ?? string.Empty;
        bool includeSenderName =
            _appConfig.IncludeSenderNameInHiddenChatKeywords;

        return _hiddenChatKeywords.Any(keyword =>
            messageText.Contains(
                keyword,
                StringComparison.OrdinalIgnoreCase) ||
            includeSenderName &&
            senderName.Contains(
                keyword,
                StringComparison.OrdinalIgnoreCase));
    }

    private bool IsMentionMessage(ChatMessage message)
    {
        if (message.ChannelType ==
                (int)Zproto.ChitChatChannelType.ChannelPrivate ||
            MatchesHiddenChatKeywords(message) ||
            !_appConfig.EnableMentionNotification ||
            _mentionKeywords.Length == 0)
        {
            return false;
        }

        string messageText = message.Message ?? string.Empty;

        return _mentionKeywords.Any(keyword =>
            messageText.Contains(
                keyword,
                StringComparison.OrdinalIgnoreCase));
    }

    private void ReevaluateMentionStatus()
    {
        foreach (ChatMessage message in _chatHistory)
        {
            message.IsMention = IsMentionMessage(message);
        }
    }

    private static string[] ParseKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return [];
        }

        char[] separators = [' ', '\u3000', '\r', '\n', '\t'];

        return keywords
            .Split(
                separators,
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void RebuildDisplayedChatMessages()
    {
        ChatMessages.Clear();

        foreach (ChatMessage message in _chatHistory)
        {
            if (ShouldDisplayChatMessage(message))
            {
                ChatMessages.Add(message);
            }
        }

        if (ChatMessages.Count > 0)
        {
            ChatListBox.ScrollIntoView(ChatMessages[^1]);
        }
    }

    private void UpdateCaptureStatus(object? sender, EventArgs e)
    {
        PacketCountText.Text =
            $"Npcap取得パケット数: {_netCap.NumSeenPackets:N0}";

        GameMessageCountText.Text =
            $"ゲームメッセージ検出数: {_netCap.NumGameMessagesSeen:N0}";

        DequeuedMessageCountText.Text =
            $"解析処理済みメッセージ数: {_netCap.NumGameMessagesDequeued:N0}";

        string lastPacketStatus =
            _netCap.LastPacketSeenAt == DateTime.MinValue
                ? "最終パケット取得時刻: 未取得"
                : $"最終パケット取得時刻: {_netCap.LastPacketSeenAt:HH:mm:ss.fff}";

        if (_netCap.CaptureDeviceSelection is { } selection)
        {
            lastPacketStatus +=
                $"\n使用中NIC: {selection.DisplayName}" +
                $"\n選択理由: {selection.SelectionReasonText}";

            if (selection.ConfiguredDeviceMissing)
            {
                lastPacketStatus +=
                    "\n警告: 設定したネットワークカードが見つかりませんでした";
            }
        }

        LastPacketTimeText.Text = lastPacketStatus;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            base.OnClosed(e);
            return;
        }

        Log.Information("Application shutdown started");
        _windowPlacementTrackingEnabled = false;

        RunShutdownAction(
            SaveWindowPlacementForShutdown,
            "Failed to save window placement during shutdown");

        RunShutdownAction(
            () => _globalHotkeyManager?.Dispose(),
            "Failed to unregister global hotkeys");

        RunShutdownAction(
            () => _windowSource?.RemoveHook(WindowMessageHook),
            "Failed to remove window message hook");

        RunShutdownAction(
            () =>
            {
                _windowPlacementSaveTimer.Stop();
                _statusTimer.Stop();
            },
            "Failed to stop window timers");

        RunShutdownAction(
            _updateCheckCancellation.Cancel,
            "Failed to cancel the update check");

        RunShutdownAction(
            () => ChatCaptureManager.ChatReceived -= OnChatReceived,
            "Failed to unsubscribe chat event");

        RunShutdownAction(
            _notificationSoundPlayer.Dispose,
            "Failed to dispose mention sound player");

        RunShutdownAction(
            _updateCheckCancellation.Dispose,
            "Failed to dispose the update check cancellation source");

        RunShutdownAction(
            _netCap.Dispose,
            "Failed to stop and dispose NetCap");

        try
        {
            base.OnClosed(e);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Base window shutdown failed");
        }
    }

    private static void RunShutdownAction(Action action, string errorMessage)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, errorMessage);
        }
    }

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));
    }

    private static IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr newValue)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new IntPtr(
                SetWindowLong32(windowHandle, index, newValue.ToInt32()));
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr windowHandle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongW",
        SetLastError = true)]
    private static extern int GetWindowLong32(
        IntPtr windowHandle,
        int index);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW",
        SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr windowHandle,
        int index,
        IntPtr newValue);

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongW",
        SetLastError = true)]
    private static extern int SetWindowLong32(
        IntPtr windowHandle,
        int index,
        int newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(
        NativePoint point,
        uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        MonitorDpiType dpiType,
        out uint dpiX,
        out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum CollapseState
    {
        Expanded,
        Collapsing,
        Collapsed,
        Expanding
    }

    private enum MonitorDpiType
    {
        Effective = 0
    }
}
