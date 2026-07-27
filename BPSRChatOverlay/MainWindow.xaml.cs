using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using BPSRChatOverlay.Config;
using BPSRChatOverlay.Managers;
using BPSRChatOverlay.Models;
using BPSR_ZDPSLib;

namespace BPSRChatOverlay;

public partial class MainWindow : Window
{
    private readonly NetCap _netCap = new();
    private readonly DispatcherTimer _statusTimer;

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

            var appConfig = ConfigManager.Load();

            ChatListBox.FontSize = Math.Clamp(appConfig.FontSize, 8, 48);
            Opacity = Math.Clamp(appConfig.Opacity, 0.2, 1.0);
            Topmost = appConfig.TopMost;

            var netCapConfig = new NetCapConfig
            {
                ExeNames = appConfig.ExeNames.ToArray(),
                CaptureDeviceName = appConfig.CaptureDeviceName
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

        _netCap.Stop();

        base.OnClosed(e);
    }
}
