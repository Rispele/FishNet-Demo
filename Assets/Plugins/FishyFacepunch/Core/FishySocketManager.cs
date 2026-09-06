using Steamworks;
using Steamworks.Data;
using System;

public class FishySocketManager : SocketManager
{
    public Action<Connection, IntPtr, int> forwardMessage;

    public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
    {
        forwardMessage(connection, data, size);
    }
}