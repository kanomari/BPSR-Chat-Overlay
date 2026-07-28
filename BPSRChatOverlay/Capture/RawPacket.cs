using System.Buffers;

namespace BPSR_ZDPSLib;

public class RawPacket
{
    public byte[] Data { get; private set; } = [];
    public int Len { get; set; }
    public DateTime LastPacketTime { get; set; } = DateTime.MinValue;
    private bool _hasRentedBuffer;

    public void Set(int len)
    {
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(len);
        Data = rentedBuffer;
        Len = len;
        _hasRentedBuffer = true;
    }
    
    public void Return()
    {
        if (!_hasRentedBuffer)
        {
            return;
        }

        byte[] rentedBuffer = Data;
        _hasRentedBuffer = false;
        Data = [];
        Len = 0;
        LastPacketTime = DateTime.MinValue;
        ArrayPool<byte>.Shared.Return(rentedBuffer);
    }
}
