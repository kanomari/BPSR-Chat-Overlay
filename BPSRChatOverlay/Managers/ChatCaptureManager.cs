using BPSR_ZDPSLib;
using BPSRChatOverlay.Models;
using Google.Protobuf;
using Serilog;
using Zproto;
using static Zproto.ChitChatNtf.Types;

namespace BPSRChatOverlay.Managers;

public static class ChatCaptureManager
{
    public static event Action<ChatMessage>? ChatReceived;

    public static void ProcessChatMessage(ReadOnlySpan<byte> payload, ExtraPacketData extraData)
    {
        ChatMessage message;

        try
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

            message = new ChatMessage
            {
                ChannelType = NormalizeChannelType(notify.VRequest.ChannelType),
                SenderName = notify.VRequest.ChatMsg.SendCharInfo.Name,
                Message = notify.VRequest.ChatMsg.MsgInfo.MsgText,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(
                ex,
                "Failed to parse or convert a chat notification. PayloadLength: {PayloadLength}",
                payload.Length);
            return;
        }

        try
        {
            ChatReceived?.Invoke(message);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(
                ex,
                "Failed to deliver a chat message to subscribers. ChannelType: {ChannelType}, HasSenderName: {HasSenderName}",
                message.ChannelType,
                !string.IsNullOrEmpty(message.SenderName));
        }
    }

    private static int NormalizeChannelType(ChitChatChannelType channelType)
    {
        return channelType == ChitChatChannelType.ChannelGroup
            ? (int)ChitChatChannelType.ChannelTeam
            : (int)channelType;
    }

    private static bool IsRecoverableException(Exception exception)
    {
        return exception is not (
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException);
    }
}
