using System.Buffers;
using Microsoft.Extensions.ObjectPool;
using PacketDotNet;
using Serilog;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using ZstdSharp;

namespace BPSR_ZDPSLib;

public class NetCap : IDisposable
{
    private const int MessageHeaderSize = 6;
    private const int MaxMessageSize = 4 * 1024 * 1024;
    private const int MaxRawPacketQueueSize = 512;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectionFilterTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ConnectionFilterCleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WarningLogInterval = TimeSpan.FromMinutes(1);
    private NetCapConfig Config;
    public ICaptureDevice? CaptureDevice;
    public TcpReassembler? TcpReassempler;

    private readonly CancellationTokenSource CancelTokenSrc = new();
    public ObjectPool<RawPacket> RawPacketPool = ObjectPool.Create(new DefaultPooledObjectPolicy<RawPacket>());
    public ConcurrentQueue<RawPacket> RawPacketQueue = new();
    private Task? PacketParseTask;
    private readonly object _connectionTasksLock = new();
    private readonly HashSet<Task> _connectionReaderTasks = [];
    private int _stopping;
    private int _disposed;
    private int _rawPacketQueueCount;
    private bool _connectionReadersStopped;
    private bool _packetParserStopped;
    private long _lastConnectionFilterCleanupTicks = DateTime.UtcNow.Ticks;
    private long _invalidLengthLastLogTicks;
    private int _invalidLengthSuppressedCount;
    private long _invalidFrameLastLogTicks;
    private int _invalidFrameSuppressedCount;
    private long _captureErrorLastLogTicks;
    private int _captureErrorSuppressedCount;
    private long _queueOverflowLastLogTicks;
    private int _queueOverflowSuppressedCount;
    private long _handlerErrorLastLogTicks;
    private int _handlerErrorSuppressedCount;
    private byte[] DecompressionScratchBuffer = new byte[1024 * 1024];
    private Decompressor _decompressor = new();
    private Dictionary<NotifyId, Action<ReadOnlySpan<byte>, ExtraPacketData>> NotifyHandlers = new();
    private ConcurrentDictionary<uint, ProxyId> ProxyReturnsDictionary = new();
    public ulong NumSeenPackets = 0;
    public DateTime LastPacketSeenAt = DateTime.MinValue;
    public int NumConnectionReaders = 0;
    public ConcurrentDictionary<ConnectionId, ConnectionFilterEntry> ConnectionFilters = new();
    public ConcurrentBag<string> ImportantLogMsgs = [];
    public ulong NumGameMessagesSeen = 0;
    public ulong NumGameMessagesDequeued = 0;
    public CaptureDeviceSelectionInfo? CaptureDeviceSelection { get; private set; }

    private bool IsDebugCaptureFileMode = false;
    private string DebugCaptureFile = "";//@"C:\Users\Xennma\Documents\BPSR_PacketCapture.pcap";
    private DateTime LastDebugCapturePacketTime = DateTime.MinValue;

    public void Init(NetCapConfig config)
    {
        Config = config;
    }

    public void Start()
    {
        if (!string.IsNullOrEmpty(DebugCaptureFile) && IsDebugCaptureFileMode)
        {
            CaptureDevice = new CaptureFileReaderDevice(DebugCaptureFile);
            CaptureDevice.Open();
        }
        else
        {
            CaptureDevice = GetCaptureDevice();

            CaptureDevice.Open(DeviceModes.Promiscuous, 100);
        }
        

        PacketParseTask = Task.Factory.StartNew(ParsePacketsLoop, CancelTokenSrc.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _ = PacketParseTask.ContinueWith(
            faultedTask => Log.Error(
                faultedTask.Exception,
                "Packet parse task stopped unexpectedly"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        TcpReassempler = new TcpReassembler();
        TcpReassempler.OnNewConnection += OnNewConnection;

        CaptureDevice.Filter = "tcp and not portrange 0-1000";
        CaptureDevice.OnPacketArrival += DeviceOnOnPacketArrival;

        CaptureDevice.StartCapture();

        Log.Information("Capture device started");
    }

    public void RegisterNotifyHandler(ulong serviceId, uint methodId, Action<ReadOnlySpan<byte>, ExtraPacketData> handler)
    {
        NotifyHandlers.Add(new NotifyId(serviceId, methodId), handler);
    }

    private void DeviceOnOnPacketArrival(object sender, PacketCapture e)
    {
        if (Volatile.Read(ref _stopping) != 0)
            return;

        try
        {
            ProcessCapturedPacket(e);
        }
        catch (OperationCanceledException) when (
            Volatile.Read(ref _stopping) != 0 ||
            CancelTokenSrc.IsCancellationRequested)
        {
            // Expected while capture is being stopped.
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            if (Volatile.Read(ref _stopping) == 0)
            {
                LogRateLimitedWarning(
                    ex,
                    "Failed to process a captured packet",
                    ref _captureErrorLastLogTicks,
                    ref _captureErrorSuppressedCount);
            }
        }
    }

    private void ProcessCapturedPacket(PacketCapture e)
    {
        var rawPacket = e.GetPacket();

        if (IsDebugCaptureFileMode)
        {
            if (LastDebugCapturePacketTime == DateTime.MinValue)
            {
                LastDebugCapturePacketTime = rawPacket.Timeval.Date;
            }
            else
            {
                TimeSpan timeDiff = rawPacket.Timeval.Date.Subtract(LastDebugCapturePacketTime);
                if (timeDiff > TimeSpan.Zero)
                {
                    System.Threading.Thread.Sleep(timeDiff);
                }

                LastDebugCapturePacketTime = rawPacket.Timeval.Date;
            }
        }

        var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

        var ipv4 = packet?.Extract<IPv4Packet>();
        if (ipv4 == null)
            return;

        var tcpPacket = packet?.Extract<TcpPacket>();
        if (tcpPacket == null)
            return;

        NumSeenPackets++;
        LastPacketSeenAt = DateTime.Now;

        if (tcpPacket.DestinationPort <= 1000 || tcpPacket.SourcePort <= 1000)
            return;

        var tcpReassembler = TcpReassempler;
        if (tcpReassembler is null)
            return;

        if (IsDebugCaptureFileMode) {
            tcpReassembler.AddPacket(ipv4, tcpPacket, rawPacket.Timeval);
            return;
        }

        var connId = new ConnectionId(ipv4.SourceAddress.ToString(), tcpPacket.SourcePort, ipv4.DestinationAddress.ToString(), tcpPacket.DestinationPort);
        DateTime utcNow = DateTime.UtcNow;
        bool allowed;

        if (ConnectionFilters.TryGetValue(connId, out var filterEntry))
        {
            allowed = filterEntry.IsAllowed;
            ConnectionFilters.TryUpdate(
                connId,
                new ConnectionFilterEntry(allowed, utcNow),
                filterEntry);
        }
        else
        {
            allowed = IsFromGame(ipv4, tcpPacket);
            ConnectionFilters.TryAdd(
                connId,
                new ConnectionFilterEntry(allowed, utcNow));
        }

        CleanupExpiredConnectionFilters(utcNow);

        if (!allowed)
            return;
        tcpReassembler.AddPacket(ipv4, tcpPacket, rawPacket.Timeval);
    }

    private void OnNewConnection(TcpReassembler.TcpConnection conn)
    {
        Task task;

        lock (_connectionTasksLock)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                conn.Dispose();
                return;
            }

            task = Task.Run(() => ReadConnectionAsync(conn));
            _connectionReaderTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_connectionTasksLock)
                {
                    _connectionReaderTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReadConnectionAsync(TcpReassembler.TcpConnection conn)
    {
        Interlocked.Increment(ref NumConnectionReaders);

        try
        {
            while (conn.IsAlive &&
                   !CancelTokenSrc.IsCancellationRequested &&
                   !conn.CancelTokenSrc.IsCancellationRequested)
            {
                var buff = await conn.Pipe.Reader
                    .ReadAtLeastAsync(6, conn.CancelTokenSrc.Token)
                    .ConfigureAwait(false);

                if (buff.IsCompleted || buff.IsCanceled)
                    break;

                Span<byte> header = new byte[6];
                buff.Buffer.Slice(0, 6).CopyTo(header);
                var len = BinaryPrimitives.ReadUInt32BigEndian(header);
                var rawMsgType = BinaryPrimitives.ReadInt16BigEndian(header[4..]);
                var msgType = (rawMsgType & 0x7FFF);

                if (len < MessageHeaderSize ||
                    len > int.MaxValue ||
                    len > MaxMessageSize)
                {
                    LogRateLimitedWarning(
                        null,
                        "Discarding a TCP connection with an invalid message length",
                        ref _invalidLengthLastLogTicks,
                        ref _invalidLengthSuppressedCount);
                    break;
                }

                conn.Pipe.Reader.AdvanceTo(buff.Buffer.Start);

                var msgBuff = await conn.Pipe.Reader
                    .ReadAtLeastAsync((int)len, conn.CancelTokenSrc.Token)
                    .ConfigureAwait(false);

                if (msgBuff.IsCompleted || msgBuff.IsCanceled)
                    break;

                RawPacket? rawPacket = RawPacketPool.Get();
                bool ownershipTransferred = false;

                try
                {
                    rawPacket.Set((int)len);
                    rawPacket.LastPacketTime = conn.LastPacketTime;
                    msgBuff.Buffer.Slice(0, len).CopyTo(
                        rawPacket.Data.AsSpan()[..(int)len]);

                    ownershipTransferred = TryEnqueueRawPacket(rawPacket);
                }
                finally
                {
                    if (!ownershipTransferred)
                    {
                        ReturnRawPacketSafely(rawPacket);
                    }
                }

                conn.Pipe.Reader.AdvanceTo(msgBuff.Buffer.GetPosition(len));
                if (ownershipTransferred)
                {
                    NumGameMessagesSeen++;
                }
            }
        }
        catch (OperationCanceledException) when (
            CancelTokenSrc.IsCancellationRequested ||
            conn.CancelTokenSrc.IsCancellationRequested)
        {
            // Expected during connection removal or application shutdown.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TCP connection reader stopped unexpectedly");
        }
        finally
        {
            Interlocked.Decrement(ref NumConnectionReaders);
            TcpReassempler?.RemoveConnection(conn);
            conn.Dispose();
            Log.Debug("TCP connection reader finished");
        }
    }
    
    private void ParsePacketsLoop()
    {
        while (!CancelTokenSrc.IsCancellationRequested)
        {
            if (RawPacketQueue.TryDequeue(out var rawPacket))
            {
                Interlocked.Decrement(ref _rawPacketQueueCount);

                try
                {
                    ParsePacket(
                        rawPacket.Data[..rawPacket.Len],
                        rawPacket.LastPacketTime);
                    NumGameMessagesDequeued++;
                }
                catch (OperationCanceledException) when (
                    CancelTokenSrc.IsCancellationRequested)
                {
                    // Expected during application shutdown.
                }
                catch (Exception ex) when (IsRecoverableException(ex))
                {
                    LogRateLimitedWarning(
                        ex,
                        "Failed to parse a queued game packet",
                        ref _invalidFrameLastLogTicks,
                        ref _invalidFrameSuppressedCount);
                }
                finally
                {
                    ReturnRawPacketSafely(rawPacket);
                }
            }
            else
            {
                CancelTokenSrc.Token.WaitHandle.WaitOne(10);
            }
        }
    }

    private void ParsePacket(ReadOnlySpan<byte> data, DateTime lastPacketTime)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            var msgData = data[offset..];
            if (msgData.Length < MessageHeaderSize)
            {
                LogInvalidFrame("Game message header is truncated");
                return;
            }

            var len = BinaryPrimitives.ReadUInt32BigEndian(msgData);
            if (len < MessageHeaderSize ||
                len > MaxMessageSize ||
                len > (uint)msgData.Length)
            {
                LogInvalidFrame("Game message length is outside the available frame");
                return;
            }

            var rawMsgType = BinaryPrimitives.ReadInt16BigEndian(msgData[4..]);
            var isCompressed = (rawMsgType & 0x8000) != 0;
            var msgType = (MsgTypeId)(rawMsgType & 0x7FFF);
            var msgPayload = msgData.Slice(6, (int)len - 6);
            offset += (int)len;

            switch (msgType)
            {
                case MsgTypeId.Notify:
                    ParseNotify(msgPayload, isCompressed, lastPacketTime);
                    break;
                case MsgTypeId.FrameDown:
                    ParseFrameDown(msgPayload, isCompressed, lastPacketTime);
                    break;
                case MsgTypeId.Call:
                    ParseCall(msgPayload, isCompressed, lastPacketTime);
                    break;
                case MsgTypeId.Return:
                    ParseReturn(msgPayload, isCompressed, lastPacketTime);
                    break;
                case MsgTypeId.FrameUp:
                    ParseFrameUp(msgPayload, isCompressed, lastPacketTime);
                    break;
                case MsgTypeId.None:
                    break;
                case MsgTypeId.Echo:
                    // Empty packets sent two at a time about once per second
                    break;
                case MsgTypeId.UNK1:
                    // Counter that increments once per 5 seconds
                    break;
                case MsgTypeId.UNK2:
                    // Counter that increments about once per second
                    break;
                default:
                    Log.Information("Got an unknown message type: {msgType}", msgType);
                    break;
            }
        }
    }

    private void ParseFrameDown(ReadOnlySpan<byte> data, bool isCompressed, DateTime lastPacketTime)
    {
        const int frameDownHeaderSize = 4;
        if (data.Length < frameDownHeaderSize)
        {
            LogInvalidFrame("FrameDown header is truncated");
            return;
        }

        var seqNum = BinaryPrimitives.ReadUInt32BigEndian(data);

        if (isCompressed)
        {
            var decompressed = Decompress(data[4..]);
            if (!decompressed.IsEmpty)
            {
                ParsePacket(decompressed, lastPacketTime);
            }
        }
        else
        {
            ParsePacket(data[4..], lastPacketTime);
        }
    }

    private void ParseNotify(ReadOnlySpan<byte> data, bool isCompressed, DateTime lastPacketTime)
    {
        const int notifyHeaderSize = 16;
        if (data.Length < notifyHeaderSize)
        {
            LogInvalidFrame("Notify header is truncated");
            return;
        }

        //byte[] debugHeaders = data.ToArray();

        var serviceUuid = BinaryPrimitives.ReadUInt64BigEndian(data);
        var stubId = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var methodId = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);

        var msgData = data[16..];
        if (isCompressed)
        {
            msgData = Decompress(msgData);

            if (msgData.IsEmpty)
            {
                Log.Logger.Warning("Error decompressing data for {serviceUuid}, {stubId}, {methodId}", serviceUuid, stubId, methodId);
                return;
            }
        }

        if (!Enum.IsDefined(typeof(EServiceId), serviceUuid))
        {
            Log.Logger.Information($"Unknown ServiceId = {serviceUuid} MethodId = {methodId}");
        }
        else
        {
            //Log.Logger.Information($"ParseNotify: S:{serviceUuid}({(EServiceId)serviceUuid}) Stub:{stubId} M:{methodId} MsgDataLen:{msgData.Length} Len={data.Length}");
        }

        var id = new NotifyId(serviceUuid, methodId);

        if (NotifyHandlers.TryGetValue(id, out var handler))
        {
            var extraData = new ExtraPacketData(lastPacketTime);

            try
            {
                handler(msgData, extraData);
            }
            catch (OperationCanceledException) when (
                CancelTokenSrc.IsCancellationRequested)
            {
                // Expected during application shutdown.
            }
            catch (Exception ex) when (IsRecoverableException(ex))
            {
                LogRateLimitedWarning(
                    ex,
                    "Notify handler failed",
                    ref _handlerErrorLastLogTicks,
                    ref _handlerErrorSuppressedCount);
            }
        }
        //Log.Information("Service UUID: {ServiceUuid}, Stub ID: {StubId}, Method ID: {MethodId}, IsCompressed: {IsCompressed}", serviceUuid, stubId, methodId, isCompressed);
    }

    private void ParseCall(ReadOnlySpan<byte> data, bool isCompressed, DateTime lastPacketTime)
    {
        const int callHeaderSize = 20;
        if (data.Length < callHeaderSize)
        {
            LogInvalidFrame("Call header is truncated");
            return;
        }

        //byte[] debugHeaders = data.ToArray();

        var proxyServiceId = BinaryPrimitives.ReadUInt64BigEndian(data);
        var subId = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var returnUid = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        var proxyMethodId = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var msgData = data[20..];

        ProxyReturnsDictionary.AddOrUpdate(returnUid, new ProxyId((uint)proxyServiceId, proxyMethodId), (key, value) => new ProxyId((uint)proxyServiceId, proxyMethodId));

        string loggedMsg = "";
        if (msgData.Length > 0)
        {
            if (msgData.Length > 50)
            {
                loggedMsg = Convert.ToHexString(msgData[0..50]);
            }
            else
            {
                loggedMsg = Convert.ToHexString(msgData);
            }
        }

        //Log.Logger.Information($"ParseCall: I:{proxyServiceId} S:{subId} R:{returnUid} M:{proxyMethodId} Len={data.Length} IsCompressed={isCompressed}{(loggedMsg.Length > 0 ? $"\nData: [{loggedMsg}]" : "")}");

    }

    private void ParseFrameUp(ReadOnlySpan<byte> data, bool isCompressed, DateTime lastPacketTime)
    {
        //byte[] debugHeaders = data.ToArray();

        const int frameHeaderSize = 4;
        const int embeddedCommonHeaderSize = 14;
        const int embeddedType2MinimumSize = 22;
        const int embeddedOtherMinimumSize = 26;

        if (data.Length < frameHeaderSize)
        {
            LogInvalidFrame("FrameUp header is truncated");
            return;
        }

        var uuid = BinaryPrimitives.ReadUInt32BigEndian(data);
        
        int offset = 4;
        int embeddedNum = 0;

        while (offset < data.Length)
        {
            int remainingLength = data.Length - offset;
            if (remainingLength < embeddedCommonHeaderSize)
            {
                LogInvalidFrame("FrameUp embedded header is truncated");
                return;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > int.MaxValue ||
                length > MaxMessageSize ||
                length > (uint)remainingLength)
            {
                LogInvalidFrame("FrameUp embedded length is outside the available frame");
                return;
            }

            int embeddedLength = (int)length;
            int endPos = offset + embeddedLength;
            offset += 4;
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            int minimumLength = flags == 2
                ? embeddedType2MinimumSize
                : embeddedOtherMinimumSize;
            if (embeddedLength < minimumLength)
            {
                LogInvalidFrame("FrameUp embedded message is shorter than its fixed header");
                return;
            }

            offset += 2;
            var padding0 = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;
            var proxyServiceId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            offset += 4;

            if (flags == 2)
            {
                var returnUid = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                offset += 4;
                var proxyMethodId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                offset += 4;
                // rest of msg data...
                ReadOnlySpan<byte> msgData = data[offset..(int)endPos];
                offset += endPos - offset;
                byte[] debugMsg = msgData.ToArray();
                // We don't store the returnUid in our dictionary because there's not going to be an actual returner for it

                //Log.Logger.Information($"ParseFrameUp: U:{uuid} L:{length} F:{flags} P0:{padding0} I:{proxyServiceId} R:{returnUid} M:{proxyMethodId} MsgDataLen={msgData.Length} Len={data.Length} IsCompressed={isCompressed}");

            }
            else
            {
                var padding1 = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                offset += 4;
                var returnUid = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                offset += 4;
                var proxyMethodId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                offset += 4;

                // rest of msg data...
                ReadOnlySpan<byte> msgData = data[offset..endPos];
                offset += endPos - offset;

                ProxyReturnsDictionary.AddOrUpdate(returnUid, new ProxyId(proxyServiceId, proxyMethodId), (key, value) => new ProxyId(proxyServiceId, proxyMethodId));

                //Log.Logger.Information($"ParseFrameUp: U:{uuid} L:{length} F:{flags} P0:{padding0} I:{proxyServiceId} P1:{padding1} R:{returnUid} M:{proxyMethodId} Len={data.Length} IsCompressed={isCompressed}");

            }

            embeddedNum++;
        }
    }

    private void ParseReturn(ReadOnlySpan<byte> data, bool isCompressed, DateTime lastPacketTime)
    {
        //byte[] debugHeaders = data.ToArray();

        if (data.Length < 12)
        {
            LogInvalidFrame("Return header is truncated");
            return;
        }

        var subId = BinaryPrimitives.ReadUInt32BigEndian(data);
        var returnUid = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var thirdId = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);

        var msgData = data[12..];

        if (isCompressed)
        {
            msgData = Decompress(msgData);

            if (msgData.IsEmpty)
            {
                Log.Logger.Warning($"Error decompressing data for {subId}, {returnUid}, {thirdId}");
                return;
            }
        }

        int protoStart = 0;
        int range = Math.Min(msgData.Length, 4);
        for (int i = 0; i < range; i++)
        {
            if (msgData[i] == 0x0A)
            {
                // Found final potential start
                protoStart = i;
            }
        }

        //Log.Logger.Information($"ParseReturn: S:{subId} R:{returnUid} T:{thirdId} Start={protoStart} Len={data.Length} IsCompressed={isCompressed}");

        ProxyReturnsDictionary.TryRemove(returnUid, out _);
    }

    private ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> data)
    {
        try
        {
            var decompressedLen = _decompressor.Unwrap(data, DecompressionScratchBuffer.AsSpan());
            return DecompressionScratchBuffer.AsSpan()[..decompressedLen];
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error decompressing data of Len: {Len}, DecompressionScratchBuffer Size: {ScratchSize}", data.Length, DecompressionScratchBuffer.Length);
            return [];
        }
    }

    private bool TryEnqueueRawPacket(RawPacket rawPacket)
    {
        if (Volatile.Read(ref _stopping) != 0)
        {
            return false;
        }

        int queueCount = Interlocked.Increment(ref _rawPacketQueueCount);
        if (queueCount > MaxRawPacketQueueSize)
        {
            Interlocked.Decrement(ref _rawPacketQueueCount);
            LogRateLimitedWarning(
                null,
                "Raw packet queue capacity reached; newest packet was discarded",
                ref _queueOverflowLastLogTicks,
                ref _queueOverflowSuppressedCount);
            return false;
        }

        if (Volatile.Read(ref _stopping) != 0)
        {
            Interlocked.Decrement(ref _rawPacketQueueCount);
            return false;
        }

        try
        {
            RawPacketQueue.Enqueue(rawPacket);
            return true;
        }
        catch
        {
            Interlocked.Decrement(ref _rawPacketQueueCount);
            throw;
        }
    }

    private void ReturnRawPacketSafely(RawPacket rawPacket)
    {
        try
        {
            rawPacket.Return();
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(ex, "Failed to return a raw packet buffer");
        }

        try
        {
            RawPacketPool.Return(rawPacket);
        }
        catch (Exception ex) when (IsRecoverableException(ex))
        {
            Log.Error(ex, "Failed to return a raw packet object");
        }
    }

    private void CleanupExpiredConnectionFilters(DateTime utcNow)
    {
        long previousCleanupTicks =
            Volatile.Read(ref _lastConnectionFilterCleanupTicks);
        if (utcNow.Ticks - previousCleanupTicks <
            ConnectionFilterCleanupInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref _lastConnectionFilterCleanupTicks,
                utcNow.Ticks,
                previousCleanupTicks) != previousCleanupTicks)
        {
            return;
        }

        DateTime expiresBefore = utcNow - ConnectionFilterTtl;
        var entries =
            (ICollection<KeyValuePair<ConnectionId, ConnectionFilterEntry>>)
            ConnectionFilters;

        foreach (var entry in ConnectionFilters)
        {
            if (entry.Value.LastSeenUtc < expiresBefore)
            {
                entries.Remove(entry);
            }
        }
    }

    private void LogInvalidFrame(string reason)
    {
        LogRateLimitedWarning(
            null,
            reason,
            ref _invalidFrameLastLogTicks,
            ref _invalidFrameSuppressedCount);
    }

    private static void LogRateLimitedWarning(
        Exception? exception,
        string message,
        ref long lastLogTicks,
        ref int suppressedCount)
    {
        long utcNowTicks = DateTime.UtcNow.Ticks;

        while (true)
        {
            long previousLogTicks = Volatile.Read(ref lastLogTicks);
            if (previousLogTicks != 0 &&
                utcNowTicks - previousLogTicks < WarningLogInterval.Ticks)
            {
                Interlocked.Increment(ref suppressedCount);
                return;
            }

            if (Interlocked.CompareExchange(
                    ref lastLogTicks,
                    utcNowTicks,
                    previousLogTicks) != previousLogTicks)
            {
                continue;
            }

            int suppressed = Interlocked.Exchange(ref suppressedCount, 0);
            if (exception is null)
            {
                Log.Warning(
                    "{WarningMessage}. SuppressedSincePreviousLog: {SuppressedCount}",
                    message,
                    suppressed);
            }
            else
            {
                Log.Warning(
                    exception,
                    "{WarningMessage}. SuppressedSincePreviousLog: {SuppressedCount}",
                    message,
                    suppressed);
            }

            return;
        }
    }

    private static bool IsRecoverableException(Exception exception)
    {
        return exception is not (
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException);
    }

    private bool IsFromGame(IPv4Packet ip, TcpPacket tcp)
    {
        var sw = Stopwatch.StartNew();
        var conns = Utils.GetTCPConnectionsForExe(Config.ExeNames);
        var isGameConnection = conns.Any((x =>
            (x.LocalAddress == ip.SourceAddress.ToString() && x.LocalPort == tcp.SourcePort) ||
            (x.RemoteAddress == ip.SourceAddress.ToString() && x.RemotePort == tcp.SourcePort) ||
            (x.LocalAddress == ip.DestinationAddress.ToString() && x.LocalPort == tcp.DestinationPort) ||
            (x.RemoteAddress == ip.DestinationAddress.ToString() && x.RemotePort == tcp.DestinationPort)));

        sw.Stop();
        Log.Logger.Debug($"Checking {ip.SourceAddress}:{tcp.SourcePort} > {ip.DestinationAddress}:{tcp.DestinationPort} is game connection: {isGameConnection}, took {sw.ElapsedMilliseconds}ms");
        
        return isGameConnection;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return;
        }

        Log.Information("NetCap stopping");

        if (CaptureDevice is { } captureDevice)
        {
            try
            {
                captureDevice.StopCapture();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop capture device");
            }

            try
            {
                captureDevice.OnPacketArrival -= DeviceOnOnPacketArrival;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to unsubscribe capture device event");
            }
        }

        if (TcpReassempler is { } tcpReassembler)
        {
            try
            {
                tcpReassembler.OnNewConnection -= OnNewConnection;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to unsubscribe TCP connection event");
            }

            try
            {
                tcpReassembler.StopAllConnections();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop TCP connections");
            }
        }

        try
        {
            CancelTokenSrc.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Stop and Dispose are idempotent.
        }

        var stopWait = Stopwatch.StartNew();
        _connectionReadersStopped = WaitForConnectionReaders(StopTimeout);
        TimeSpan remainingTimeout = StopTimeout - stopWait.Elapsed;
        if (remainingTimeout < TimeSpan.Zero)
        {
            remainingTimeout = TimeSpan.Zero;
        }

        _packetParserStopped = WaitForPacketParser(remainingTimeout);

        try
        {
            DrainRawPacketQueue();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to drain queued packets during shutdown");
        }

        if (CaptureDevice is { } deviceToClose)
        {
            try
            {
                deviceToClose.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to close capture device");
            }

            try
            {
                (deviceToClose as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to dispose capture device");
            }
        }

        ConnectionFilters.Clear();
        Log.Information("NetCap stopped");
    }

    private bool WaitForConnectionReaders(TimeSpan timeout)
    {
        Task[] tasks;
        lock (_connectionTasksLock)
        {
            tasks = _connectionReaderTasks.ToArray();
        }

        if (!WaitForTasks(Task.WhenAll(tasks), timeout))
        {
            Log.Warning(
                "Timed out waiting for TCP connection readers to stop. RemainingTaskCount: {RemainingTaskCount}",
                tasks.Count(task => !task.IsCompleted));
            return false;
        }

        Log.Information("TCP connection readers stopped");
        return true;
    }

    private bool WaitForPacketParser(TimeSpan timeout)
    {
        if (PacketParseTask is null)
        {
            return true;
        }

        if (!WaitForTasks(PacketParseTask, timeout))
        {
            Log.Warning(
                "Timed out waiting for packet parse task to stop. Status: {TaskStatus}",
                PacketParseTask.Status);
            return false;
        }

        if (PacketParseTask.IsFaulted)
        {
            return true;
        }

        Log.Information("Packet parse task stopped");
        return true;
    }

    private static bool WaitForTasks(Task task, TimeSpan timeout)
    {
        try
        {
            return Task.WhenAny(task, Task.Delay(timeout))
                .GetAwaiter()
                .GetResult() == task;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed while waiting for background task shutdown");
            return true;
        }
    }

    private void DrainRawPacketQueue()
    {
        int discardedPackets = 0;

        while (RawPacketQueue.TryDequeue(out var rawPacket))
        {
            Interlocked.Decrement(ref _rawPacketQueueCount);
            ReturnRawPacketSafely(rawPacket);
            discardedPackets++;
        }

        if (discardedPackets > 0)
        {
            Log.Warning(
                "Discarded queued packets during shutdown. Count: {DiscardedPacketCount}",
                discardedPackets);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Stop();

        if (_connectionReadersStopped && _packetParserStopped)
        {
            CancelTokenSrc.Dispose();
            _decompressor.Dispose();
        }
        else
        {
            Log.Warning(
                "Background tasks did not stop before timeout; task-owned resources were not disposed to avoid use-after-dispose");
        }
    }

    public void PrintCaptureDevices()
    {
        var devices = CaptureDeviceList.Instance;
        foreach (var device in devices)
        {
            Log.Information(
                "Device: {DeviceName}, {FriendlyName}, {Description}",
                device.Name,
                GetFriendlyName(device),
                device.Description);
        }
    }

    private ICaptureDevice GetCaptureDevice()
    {
        CaptureDeviceList devices;

        try
        {
            devices = CaptureDeviceList.Instance;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enumerating capture devices");
            throw;
        }

        if (devices.Count == 0)
        {
            var exception = new InvalidOperationException(
                "利用可能なネットワークカードが見つかりません。Npcapの導入状態を確認してください。");
            Log.Error(exception, "No capture devices were found");
            throw exception;
        }

        try
        {
            CaptureDeviceSelectionResult selection =
                CaptureDeviceSelector.Select(
                    devices.Cast<ICaptureDevice>().ToList(),
                    Config.CaptureDeviceName,
                    Config.ExeNames);
            return SelectCaptureDevice(selection);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error selecting capture device");
            throw;
        }
    }

    private ICaptureDevice SelectCaptureDevice(
        CaptureDeviceSelectionResult selection)
    {
        ICaptureDevice device = selection.Device;
        string actualDeviceName = device.Name ?? string.Empty;
        string? friendlyName = NormalizeDeviceText(GetFriendlyName(device));
        string? description = NormalizeDeviceText(device.Description);
        string displayName = CreateCaptureDeviceDisplayName(
            actualDeviceName,
            friendlyName,
            description);

        CaptureDeviceSelection = new CaptureDeviceSelectionInfo(
            actualDeviceName,
            friendlyName,
            description,
            displayName,
            selection.SelectionReason,
            selection.WasFallback,
            selection.ConfiguredDeviceMissing,
            selection.WasConfigurationEmpty,
            selection.ConfiguredDeviceName,
            selection.GameConnectionLocalAddress?.ToString(),
            selection.WindowsBestRouteInterfaceIndex);

        Log.Information(
            "Capture device selected. SelectionReason: {SelectionReason}, ConfiguredDeviceName: {ConfiguredDeviceName}, ActualDeviceName: {ActualDeviceName}, FriendlyName: {FriendlyName}, Description: {Description}, DisplayName: {DisplayName}, WasFallback: {WasFallback}, ConfiguredDeviceMissing: {ConfiguredDeviceMissing}, WasConfigurationEmpty: {WasConfigurationEmpty}, GameConnectionLocalAddress: {GameConnectionLocalAddress}, WindowsBestRouteInterfaceIndex: {WindowsBestRouteInterfaceIndex}",
            CaptureDeviceSelection.SelectionReason,
            CaptureDeviceSelection.ConfiguredDeviceName,
            CaptureDeviceSelection.ActualDeviceName,
            CaptureDeviceSelection.FriendlyName,
            CaptureDeviceSelection.Description,
            CaptureDeviceSelection.DisplayName,
            CaptureDeviceSelection.WasFallback,
            CaptureDeviceSelection.ConfiguredDeviceMissing,
            CaptureDeviceSelection.WasConfigurationEmpty,
            CaptureDeviceSelection.GameConnectionLocalAddress,
            CaptureDeviceSelection.WindowsBestRouteInterfaceIndex);

        if (selection.ConfiguredDeviceMissing)
        {
            Log.Warning(
                "Configured capture device was not found. ConfiguredDeviceName: {ConfiguredDeviceName}, FallbackReason: {FallbackReason}, ActualDeviceName: {ActualDeviceName}, FriendlyName: {FriendlyName}, Description: {Description}",
                selection.ConfiguredDeviceName,
                CaptureDeviceSelection.SelectionReason,
                actualDeviceName,
                friendlyName,
                description);
        }

        return device;
    }

    private static string? GetFriendlyName(ICaptureDevice device)
    {
        return device is LibPcapLiveDevice liveDevice
            ? liveDevice.Interface?.FriendlyName
            : null;
    }

    private static string CreateCaptureDeviceDisplayName(
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

        return friendlyName ??
               description ??
               NormalizeDeviceText(name) ??
               "名前不明のネットワークカード";
    }

    private static string? NormalizeDeviceText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string GetFilterString(IEnumerable<TcpHelper.TcpRow> conns)
    {
        var connLines = conns.DistinctBy(x => x.RemoteAddress).Select(x => $"(tcp and src host {x.RemoteAddress} or dst host {x.RemoteAddress})");
        var filterStr = string.Join(" or ", connLines);
        return filterStr;
    }
}

public class PendingConnState(IPAddress addr)
{
    public IPAddress IPAddress { get; set; } = addr;
    public DateTime FirstSeenAt { get; set; } = DateTime.Now;
    public bool? IsGameConnection = null;
}

public readonly record struct ConnectionFilterEntry(
    bool IsAllowed,
    DateTime LastSeenUtc);
