#if !FishyFacepunch
using FishNet.Transporting;
using FishyFacepunch.Client;
using Steamworks;
using Steamworks.Data;
using System;
using System.Collections.Generic;
using FishNet.Managing;

namespace FishyFacepunch.Server
{
    public class ServerSocket : CommonSocket
    {
        #region Public.
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            //Remote clients can only have Started or Stopped states since we cannot know in between.
            if (steamConnections.Second.ContainsKey(connectionId))
                return RemoteConnectionState.Started;
            else
                return RemoteConnectionState.Stopped;
        }
        #endregion

        #region Private.
        /// <summary>
        /// SteamConnections for ConnectionIds.
        /// </summary>
        private BidirectionalDictionary<Connection, int> steamConnections = new BidirectionalDictionary<Connection, int>();
        /// <summary>
        /// SteamIds for ConnectionIds.
        /// </summary>
        private BidirectionalDictionary<SteamId, int> steamIds = new BidirectionalDictionary<SteamId, int>();
        /// <summary>
        /// Maximum number of remote connections.
        /// </summary>
        private int maximumClients;
        /// <summary>
        /// Next Id to use for a connection.
        /// </summary>
        private int nextConnectionId;
        /// <summary>
        /// Socket for the connection.
        /// </summary>
        private FishySocketManager socket;
        /// <summary>
        /// ConnectionIds which can be reused.
        /// </summary>
        private Queue<int> cachedConnectionIds = new Queue<int>();
        /// <summary>
        /// Contains state of the client host. True is started, false is stopped.
        /// </summary>
        private bool clientHostStarted = false;
        /// <summary>
        /// Packets received from local client.
        /// </summary>
        private Queue<LocalPacket> clientHostIncoming = new Queue<LocalPacket>();
        /// <summary>
        /// Socket for client host. Will be null if not being used.
        /// </summary>
        private ClientHostSocket clientHost;
        #endregion

        /// <summary>
        /// Initializes this for use.
        /// </summary>
        /// <param name="t"></param>
        internal override void Initialize(Transport t)
        {
            base.Initialize(t);
        }

        /// <summary>
        /// Resets the socket if invalid.
        /// </summary>
        internal void ResetInvalidSocket()
        {
            /* Force connection state to stopped if listener is invalid.
            * Not sure if steam may change this internally so better
            * safe than sorry and check before trying to connect
            * rather than being stuck in the incorrect state. */
            if (socket == default)
                base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
        }
        /// <summary>
        /// Starts the server.
        /// </summary>
        internal bool StartConnection(string address, ushort port, int maximumClients)
        {
            SteamNetworkingSockets.OnConnectionStatusChanged += OnRemoteConnectionState;

            SetMaximumClients(maximumClients);
            nextConnectionId = 0;
            cachedConnectionIds.Clear();

            base.SetLocalConnectionState(LocalConnectionState.Starting, true);
            
            if (socket != null)
            {
                socket?.Close();
                socket = default;
            }

#if UNITY_SERVER
            _socket = SteamNetworkingSockets.CreateNormalSocket<FishySocketManager>(NetAddress.From(address, port));
#else
            socket = SteamNetworkingSockets.CreateRelaySocket<FishySocketManager>();
#endif
            socket.forwardMessage = OnMessageReceived;

            base.SetLocalConnectionState(LocalConnectionState.Started, true);

            return true;
        }

        /// <summary>
        /// Stops the local socket.
        /// </summary>
        internal bool StopConnection()
        {
            if (base.GetLocalConnectionState() == LocalConnectionState.Stopped)
                return false;

            base.SetLocalConnectionState(LocalConnectionState.Stopping, true);

            if (socket != null)
            {
                SteamNetworkingSockets.OnConnectionStatusChanged -= OnRemoteConnectionState;
                socket?.Close();
                socket = default;
            }
            
            base.SetLocalConnectionState(LocalConnectionState.Stopped, true);

            return true;
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        internal bool StopConnection(int connectionId)
        {
            if (connectionId == FishyFacepunch.ClientHostID)
            {
                if (clientHost != null)
                {
                    clientHost.StopConnection();
                    return true;
                }
                else
                {
                    return false;
                }

            }
            else
            {
                if (steamConnections.Second.TryGetValue(connectionId, out Connection steamConn))
                {
                    return StopConnection(connectionId, steamConn);
                }
                else
                {
                    transport.NetworkManager.LogError($"Steam connection not found for connectionId {connectionId}.");
                    return false;
                }
            }            
        }
        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="socket"></param>
        private bool StopConnection(int connectionId, Connection socket)
        {
            socket.Close(false, 0, "Graceful disconnect");
            steamConnections.Remove(connectionId);
            steamIds.Remove(connectionId);
            transport.NetworkManager.Log($"Client with ConnectionID {connectionId} disconnected.");
            base.transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, connectionId, transport.Index));
            cachedConnectionIds.Enqueue(connectionId);

            return true;
        }

        /// <summary>
        /// Called when a remote connection state changes.
        /// </summary>
        private void OnRemoteConnectionState(Connection conn, ConnectionInfo info)
        {
            ulong clientSteamID = info.Identity.SteamId;
            if (info.State == ConnectionState.Connecting)
            {
                if (steamConnections.Count >= maximumClients)
                {
                    transport.NetworkManager.Log($"Incoming connection {clientSteamID} was rejected because would exceed the maximum connection count.");

                    conn.Close(false, 0, "Max Connection Count");
                    return;
                }

                Result res;

                if ((res = conn.Accept()) == Result.OK)
                {
                    transport.NetworkManager.Log($"Accepting connection {clientSteamID}");
                }
                else
                {
                    transport.NetworkManager.Log($"Connection {clientSteamID} could not be accepted: {res.ToString()}");
                }
            }
            else if (info.State == ConnectionState.Connected)
            {
                int connectionId = (cachedConnectionIds.Count > 0) ? cachedConnectionIds.Dequeue() : nextConnectionId++;
                steamConnections.Add(conn, connectionId);
                steamIds.Add(clientSteamID, connectionId);

                transport.NetworkManager.Log($"Client with SteamID {clientSteamID} connected. Assigning connection id {connectionId}");
                base.transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started, connectionId, transport.Index));
            }
            else if (info.State == ConnectionState.ClosedByPeer || info.State == ConnectionState.ProblemDetectedLocally)
            {
                if (steamConnections.TryGetValue(conn, out int connId))
                {
                    StopConnection(connId, conn);
                }
            }
            else
            {
                transport.NetworkManager.Log($"Connection {clientSteamID} state changed: {info.State.ToString()}");
            }
        }


        /// <summary>
        /// Allows for Outgoing queue to be iterated.
        /// </summary>
        internal void IterateOutgoing()
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            foreach (Connection conn in steamConnections.FirstTypes)
            {
                conn.Flush();
            }
        }

        /// <summary>
        /// Iterates the Incoming queue.
        /// </summary>
        /// <param name="transport"></param>
        internal void IterateIncoming()
        {
            //Stopped or trying to stop.
            if (base.GetLocalConnectionState() == LocalConnectionState.Stopped || base.GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;

            //Iterate local client packets first.
            while (clientHostIncoming.Count > 0)
            {
                LocalPacket packet = clientHostIncoming.Dequeue();
                ArraySegment<byte> segment = new ArraySegment<byte>(packet.data, 0, packet.length);
                base.transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(segment, (Channel)packet.channel, FishyFacepunch.ClientHostID, transport.Index));
                packet.Dispose();
            }

            socket.Receive(MaxMessages);
        }

        private void OnMessageReceived(Connection conn, IntPtr dataPtr, int size)
        {
            (byte[] data, int ch) = ProcessMessage(dataPtr, size);
            base.transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(new ArraySegment<byte>(data), (Channel)ch, steamConnections[conn], transport.Index));
        }

        /// <summary>
        /// Sends data to a client.
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="segment"></param>
        /// <param name="connectionId"></param>
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (base.GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            //Check if sending local client first, send and exit if so.
            if (connectionId == FishyFacepunch.ClientHostID)
            {
                if (clientHost != null)
                {
                    LocalPacket packet = new LocalPacket(segment, channelId);
                    clientHost.ReceivedFromLocalServer(packet);
                }
                return;
            }

            if (steamConnections.TryGetValue(connectionId, out Connection steamConn))
            {
                Result res = base.Send(steamConn, segment, channelId);

                if (res == Result.NoConnection || res == Result.InvalidParam)
                {
                    transport.NetworkManager.Log($"Connection to {connectionId} was lost.");
                    StopConnection(connectionId, steamConn);
                }
                else if (res != Result.OK)
                {
                    transport.NetworkManager.LogError($"Could not send: {res.ToString()}");
                }
            }
            else
            {
                transport.NetworkManager.LogError($"ConnectionId {connectionId} does not exist, data will not be sent.");
            }
        }

        /// <summary>
        /// Gets the address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        internal string GetConnectionAddress(int connectionId)
        {
            if (steamIds.TryGetValue(connectionId, out SteamId steamId))
            {
                return steamId.ToString();
            }
            else
            {
                transport.NetworkManager.LogError($"ConnectionId {connectionId} is invalid; address cannot be returned.");

                return string.Empty;
            }
        }


        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        internal void SetMaximumClients(int value)
        {
            maximumClients = Math.Min(value, FishyFacepunch.ClientHostID - 1);
        }
        internal int GetMaximumClients()
        {
            return maximumClients;
        }

        #region ClientHost (local client).
        /// <summary>
        /// Sets ClientHost value.
        /// </summary>
        /// <param name="socket"></param>
        internal void SetClientHostSocket(ClientHostSocket socket)
        {
            clientHost = socket;
        }
        /// <summary>
        /// Called when the local client stops.
        /// </summary>
        internal void OnClientHostState(bool started)
        {

            clientHostStarted = started;
            FishyFacepunch ff = (FishyFacepunch)base.transport;
            SteamId steamId = new SteamId()
            {
                Value = ff.localUserSteamID
            };

            //If not started flush incoming from local client.
            if (!started && clientHostStarted)
            {
                base.ClearQueue(clientHostIncoming);
                base.transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped, FishyFacepunch.ClientHostID, transport.Index));
                steamIds.Remove(steamId);
            }
            //If started.
            else if (started)
            {
                steamIds[steamId] = FishyFacepunch.ClientHostID;
                base.transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started, FishyFacepunch.ClientHostID, transport.Index));
            }

            clientHostStarted = started;
        }

        /// <summary>
        /// Queues a received packet from the local client.
        /// </summary>
        internal void ReceivedFromClientHost(LocalPacket packet)
        {
            if (!clientHostStarted)
            {
                packet.Dispose();
                return;
            }

            clientHostIncoming.Enqueue(packet);
        }
        #endregion
    }
}
#endif // !DISABLESTEAMWORKS