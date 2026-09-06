#if !FishyFacepunch
using FishNet.Transporting;
using FishyFacepunch.Server;
using System;
using System.Collections.Generic;

namespace FishyFacepunch.Client
{
    /// <summary>
    /// Creates a fake client connection to interact with the ServerSocket when acting as host.
    /// </summary>
    public class ClientHostSocket : CommonSocket
    {
        #region Private.
        /// <summary>
        /// Socket for the server.
        /// </summary>
        private ServerSocket server;
        /// <summary>
        /// Incomimg data.
        /// </summary>
        private Queue<LocalPacket> incoming = new Queue<LocalPacket>();
        #endregion

        /// <summary>
        /// Checks to set localCLient started.
        /// </summary>
        internal void CheckSetStarted()
        {
            //Check to set as started.
            if (server != null && base.GetLocalConnectionState() == LocalConnectionState.Starting)
            {
                if (server.GetLocalConnectionState() == LocalConnectionState.Started)
                    SetLocalConnectionState(LocalConnectionState.Started, false);
            }
        }

        /// <summary>
        /// Starts the client connection.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="channelsCount"></param>
        /// <param name="pollTime"></param>
        internal bool StartConnection(ServerSocket serverSocket)
        {
            server = serverSocket;
            server.SetClientHostSocket(this);
            if (server.GetLocalConnectionState() != LocalConnectionState.Started)
                return false;

            SetLocalConnectionState(LocalConnectionState.Starting, false);
            return true;
        }

        /// <summary>
        /// Sets a new connection state.
        /// </summary>
        protected override void SetLocalConnectionState(LocalConnectionState connectionState, bool server)
        {
            base.SetLocalConnectionState(connectionState, server);
            if (connectionState == LocalConnectionState.Started)
                this.server.OnClientHostState(true);
            else
                this.server.OnClientHostState(false);
        }

        /// <summary>
        /// Stops the local socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (base.GetLocalConnectionState() == LocalConnectionState.Stopped || base.GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            base.ClearQueue(incoming);
            //Immediately set stopped since no real connection exists.
            SetLocalConnectionState(LocalConnectionState.Stopping, false);
            SetLocalConnectionState(LocalConnectionState.Stopped, false);
            server.SetClientHostSocket(null);

            return true;
        }

        /// <summary>
        /// Iterations data received.
        /// </summary>
        internal void IterateIncoming()
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            while (incoming.Count > 0)
            {
                LocalPacket packet = incoming.Dequeue();
                ArraySegment<byte> segment = new ArraySegment<byte>(packet.data, 0, packet.length);
                base.transport.HandleClientReceivedDataArgs(new ClientReceivedDataArgs(segment, (Channel)packet.channel, transport.Index));
                packet.Dispose();
            }
        }

        /// <summary>
        /// Called when the server sends the local client data.
        /// </summary>
        internal void ReceivedFromLocalServer(LocalPacket packet)
        {
            incoming.Enqueue(packet);
        }

        /// <summary>
        /// Queues data to be sent to server.
        /// </summary>
        internal void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return;
            if (server.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            LocalPacket packet = new LocalPacket(segment, channelId);
            server.ReceivedFromClientHost(packet);
        }
    }
}
#endif // !DISABLESTEAMWORKS