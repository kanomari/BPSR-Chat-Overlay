using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.Managers;
using BPSRChatOverlay.Models;
using BPSR_ZDPSLib;

namespace BPSRChatOverlay;

public partial class MainWindow : Window
{
    private const int MaxChatMessageCount = 500;
    private const int ClickThroughHotKeyId = 0x4250;
    private const int GwlExStyle = -20;
    private const int WmHotKey = 0x0312;
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint VkF10 = 0x79;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExLayered = 0x00080000L;

    private readonly NetCap _netCap = new();
    private readonly DispatcherTimer _statusTimer;
    private AppConfig _appConfig = new();
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private bool _clickThroughHotKeyRegistered;

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;

        /*
         * NetCapの解析処理はバックグラウンドスレッドで動きます。
         * 受信イベントを画面へ接続します。
         */
        ChatCaptureManager.ChatReceived += OnChatReceived;

        try
        {
            CaptureStatusText.Text = "設定ファイルを読み込んでいます...";

            _appConfig = ConfigManager.Load();

            ApplyDisplaySettings(_appConfig);

            var netCapConfig = new NetCapConfig
            {
                ExeNames = _appConfig.ExeNames.ToArray(),
                CaptureDeviceName = _appConfig.CaptureDeviceName
            };

            CaptureStatusText.Text = "Npcapを初期化しています...";

            _netCap.Init(netCapConfig);

            ChatCaptureBootstrap.Initialize(_netCap);

            _netCap.Start();

            CaptureStatusText.Text =
                "パケット取得を開始しました。ゲーム内チャットを送信してください。";
        }
        catch (Exception ex)
        {
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
    }

    private void ApplyDisplaySettings(AppConfig config)
    {
        ChatListBox.FontSize = Math.Clamp(config.FontSize, 8, 48);
        ChatBackgroundBorder.Opacity =
            Math.Clamp(config.BackgroundOpacity, 0.0, 1.0);
        Resources["ChatTextOpacity"] =
            Math.Clamp(config.TextOpacity, 0.0, 1.0);
        MenuBackgroundBorder.Opacity =
            Math.Clamp(config.MenuBackgroundOpacity, 0.0, 1.0);
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
            ConfigManager.Save(savedConfig);
            _appConfig = savedConfig;
            ApplyDisplaySettings(_appConfig);
        }
    }

    private void MenuDragArea_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // マウスボタンが既に離された場合は移動を中止します。
        }
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

        return IntPtr.Zero;
    }

    private static void SaveConfigSafely(AppConfig config)
    {
        try
        {
            ConfigManager.Save(config);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save config.json: {ex}");
        }
    }

    private void OnChatReceived(ChatMessage message)
    {
        /*
         * ChatReceivedは画面とは別のスレッドから呼ばれる可能性があります。
         * WPFの画面更新はUIスレッドで行う必要があるため、
         * Dispatcherを経由します。
         */
        Dispatcher.BeginInvoke(() =>
        {
            ChatMessages.Add(message);

            while (ChatMessages.Count > MaxChatMessageCount)
            {
                ChatMessages.RemoveAt(0);
            }

            ChatCountText.Text =
                $"受信件数: {ChatMessages.Count:N0}";

            ChatListBox.ScrollIntoView(message);
        });
    }

    private void UpdateCaptureStatus(object? sender, EventArgs e)
    {
        PacketCountText.Text =
            $"Npcap取得パケット数: {_netCap.NumSeenPackets:N0}";

        GameMessageCountText.Text =
            $"ゲームメッセージ検出数: {_netCap.NumGameMessagesSeen:N0}";

        DequeuedMessageCountText.Text =
            $"解析処理済みメッセージ数: {_netCap.NumGameMessagesDequeued:N0}";

        LastPacketTimeText.Text =
            _netCap.LastPacketSeenAt == DateTime.MinValue
                ? "最終パケット取得時刻: 未取得"
                : $"最終パケット取得時刻: {_netCap.LastPacketSeenAt:HH:mm:ss.fff}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();

        ChatCaptureManager.ChatReceived -= OnChatReceived;

        if (_clickThroughHotKeyRegistered)
        {
            UnregisterHotKey(_windowHandle, ClickThroughHotKeyId);
            _clickThroughHotKeyRegistered = false;
        }

        _windowSource?.RemoveHook(WindowMessageHook);

        _netCap.Stop();

        base.OnClosed(e);
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
}
