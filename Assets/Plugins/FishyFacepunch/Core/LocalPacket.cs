using FishNet.Utility.Performance;
using System;

namespace FishyFacepunch
{
    internal struct LocalPacket
    {
        public byte[] data;
        public int length;
        public byte channel;
        public LocalPacket(ArraySegment<byte> data, byte channel)
        {
            this.data = ByteArrayPool.Retrieve(data.Count);
            length = data.Count;
            Buffer.BlockCopy(data.Array, data.Offset, this.data, 0, length);
            this.channel = channel;
        }

        public void Dispose()
        {
            if (data != null)
                ByteArrayPool.Store(data);
        }
    }
}
