using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
    private const int ClickThroughHotKeyId = 0x4250;
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
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint VkF10 = 0x79;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint DefaultDpi = 96;
    private const double ResizeBorderWidthDip = 8.0;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;
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
    private string[] _mentionKeywords = [];
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private bool _clickThroughHotKeyRegistered;
    private bool _windowPlacementTrackingEnabled;
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

        _clickThroughHotKeyRegistered = RegisterHotKey(
            _windowHandle,
            ClickThroughHotKeyId,
            ModControl | ModShift,
            VkF10);

        if (!_clickThroughHotKeyRegistered)
        {
            Debug.WriteLine("Failed to register Ctrl + Shift + F10.");
        }

        ApplyDisplaySettings(_appConfig);
        _windowPlacementTrackingEnabled = true;
    }

    private void ApplyDisplaySettings(AppConfig config)
    {
        _chatFilterKeywords = ParseKeywords(config.ChatFilterKeywords);
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
        DebugPanel.Visibility = config.ShowDebugPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChatTogglePanel.Visibility = config.ShowChatToggleButtons
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateChatToggleButtonStates();
        UpdateFeatureToggleButtonStates();
        Topmost = config.TopMost;

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (config.ClickThrough && !_clickThroughHotKeyRegistered)
        {
            config.ClickThrough = false;
            SaveConfigSafely(config);
        }

        ApplyClickThrough(config.ClickThrough);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_appConfig)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true &&
            settingsWindow.SavedConfig is { } savedConfig)
        {
            if (!SaveConfigSafely(savedConfig))
            {
                MessageBox.Show(
                    this,
                    "設定を保存できませんでした。ログを確認してください。",
                    "BPSR Chat Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
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
        if (e.ChangedButton != MouseButton.Left ||
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
            wParam.ToInt32() == ClickThroughHotKeyId)
        {
            _appConfig.ClickThrough = !_appConfig.ClickThrough;
            ApplyClickThrough(_appConfig.ClickThrough);
            SaveConfigSafely(_appConfig);
            handled = true;
        }
        else if (message == WmNcHitTest &&
                 !_appConfig.ClickThrough)
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
            WindowState == WindowState.Minimized)
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
        if (WindowState == WindowState.Minimized)
        {
            return;
        }

        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        if (!IsValidWindowSize(bounds.Width, bounds.Height) ||
            !double.IsFinite(bounds.Left) ||
            !double.IsFinite(bounds.Top))
        {
            return;
        }

        _appConfig.WindowLeft = bounds.Left;
        _appConfig.WindowTop = bounds.Top;
        _appConfig.WindowWidth = bounds.Width;
        _appConfig.WindowHeight = bounds.Height;

        SaveConfigSafely(_appConfig);
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

        return message.ChannelType ==
            (int)Zproto.ChitChatChannelType.ChannelPrivate
            ? matchesChannelFilter
            : matchesChannelFilter && MatchesKeywordFilter(message);
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

    private bool IsMentionMessage(ChatMessage message)
    {
        if (message.ChannelType ==
                (int)Zproto.ChitChatChannelType.ChannelPrivate ||
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
            SaveWindowPlacement,
            "Failed to save window placement during shutdown");

        RunShutdownAction(
            () =>
            {
                if (!_clickThroughHotKeyRegistered)
                {
                    return;
                }

                if (!UnregisterHotKey(_windowHandle, ClickThroughHotKeyId))
                {
                    Log.Warning("Failed to unregister click-through hot key");
                }

                _clickThroughHotKeyRegistered = false;
            },
            "Failed to unregister click-through hot key");

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
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(
        IntPtr windowHandle,
        int id);

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

    private enum MonitorDpiType
    {
        Effective = 0
    }
}
