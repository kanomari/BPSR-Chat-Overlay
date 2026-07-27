using BPSR_ZDPSLib;
using Zproto;

namespace BPSRChatOverlay.Managers;

public static class ChatCaptureBootstrap
{
    public static void Initialize(NetCap netCap)
    {
        netCap.RegisterNotifyHandler(
            (ulong)EServiceId.ChitChatNtf,
            (uint)BPSR_ZDPSLib.ServiceMethods.ChitChatNtf.NotifyNewestChitChatMsgs,
            ChatCaptureManager.ProcessChatMessage);
    }
}