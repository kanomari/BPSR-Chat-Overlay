using BPSR_ZDPSLib;
using BPSRChatOverlay.Models;
using Google.Protobuf;
using Zproto;
using static Zproto.ChitChatNtf.Types;

namespace BPSRChatOverlay.Managers;

public static class ChatCaptureManager
{
    public static event Action<ChatMessage>? ChatReceived;

    public static void ProcessChatMessage(ReadOnlySpan<byte> payload, ExtraPacketData extraData)
    {
        var notify = NotifyNewestChitChatMsgs.Parser.ParseFrom(payload);

        if (notify == null)
        {
            return;
        }

        // ゲーム内専用絵文字はOverlayでは表示しない
        if (notify.VRequest.ChatMsg.MsgInfo.MsgType ==
            ChitChatMsgType.ChatMsgPictureEmoji)
        {
            return;
        }

        ChatMessage message = new()
        {
            ChannelType = (int)notify.VRequest.ChannelType,
            SenderName = notify.VRequest.ChatMsg.SendCharInfo.Name,
            Message = notify.VRequest.ChatMsg.MsgInfo.MsgText,
            Timestamp = DateTime.Now
        };

        ChatReceived?.Invoke(message);
    }
}
