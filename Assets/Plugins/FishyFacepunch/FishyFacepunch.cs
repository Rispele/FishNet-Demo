#if !FishyFacepunch
using FishNet.Managing;
using FishNet.Transporting;
using Steamworks;
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace FishyFacepunch
{
    public class FishyFacepunch : Transport
    {
        ~FishyFacepunch()
        {
            Shutdown();
        }

        #region Public.
        [System.NonSerialized]
        public ulong localUserSteamID;
        #endregion

        #region Serialized.
        /// <summary>
        /// Steam application Id.
        /// </summary>
        [FormerlySerializedAs("_steamAppID")]
        [Tooltip("Steam application Id.")]
        [SerializeField]
        private uint steamAppID = 480;

        [FormerlySerializedAs("_serverBindAddress")]
        [Header("Server")]
        /// <summary>
        /// Address server should bind to.
        /// </summary>
        [Tooltip("Address server should bind to.")]
        [SerializeField]
        private string serverBindAddress = string.Empty;
        /// <summary>
        /// Port to use.
        /// </summary>
        [FormerlySerializedAs("_port")]
        [Tooltip("Port to use.")]
        [SerializeField]
        private ushort port = 27015;
        /// <summary>
        /// Maximum number of players which may be connected at once.
        /// </summary>
        [FormerlySerializedAs("_maximumClients")]
        [Tooltip("Maximum number of players which may be connected at once.")]
        [Range(1, ushort.MaxValue)]
        [SerializeField]
        private ushort maximumClients = 16;

        [FormerlySerializedAs("_clientAddress")]
        [Header("Client")]
        /// <summary>
        /// Address client should connect to.
        /// </summary>
        [Tooltip("Address client should connect to.")]
        [SerializeField]
        private string clientAddress = string.Empty;

        [FormerlySerializedAs("_timeout")]
        [Tooltip("Timeout for connecting in seconds.")]
        [SerializeField]
        private int timeout = 25;
        #endregion

        #region Private.
        /// <summary>
        /// MTUs for each channel.
        /// </summary>
        private int[] mtus;
        /// <summary>
        /// Client for the transport.
        /// </summary>
        private Client.ClientSocket client = new Client.ClientSocket();
        /// <summary>
        /// Client when acting as host.
        /// </summary>
        private Client.ClientHostSocket clientHost = new Client.ClientHostSocket();
        /// <summary>
        /// Server for the transport.
        /// </summary>
        private Server.ServerSocket server = new Server.ServerSocket();
        #endregion

        #region Const.
        /// <summary>
        /// Id to use for client when acting as host.
        /// </summary>
        internal const int ClientHostID = short.MaxValue;
        #endregion

        #region Initialization and Unity.
        public override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            base.Initialize(networkManager, transportIndex);

            CreateChannelData();

#if !UNITY_SERVER
            if (!SteamClient.IsValid) //Steam might have already been initialized by something else
            {
                SteamClient.Init(steamAppID, true);
            }

            SteamNetworking.AllowP2PPacketRelay(true);
#endif
            clientHost.Initialize(this);
            client.Initialize(this);
            server.Initialize(this);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Update()
        {
            clientHost.CheckSetStarted();
        }
        #endregion

        #region Setup.
        /// <summary>
        /// Creates ChannelData for the transport.
        /// </summary>
        private void CreateChannelData()
        {
            mtus = new int[2]
            {
                1048576,
                1200
            };
        }

        /// <summary>
        /// Tries to initialize steam network access.
        /// </summary>
        private void InitializeRelayNetworkAccess()
        {
#if !UNITY_SERVER
            SteamNetworkingUtils.InitRelayNetworkAccess();
            localUserSteamID = Steamworks.SteamClient.SteamId.Value;
#endif
        }
        #endregion

        #region ConnectionStates.
        /// <summary>
        /// Gets the IP address of a remote connection Id.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public override string GetConnectionAddress(int connectionId)
        {
            return server.GetConnectionAddress(connectionId);
        }
        /// <summary>
        /// Called when a connection state changes for the local client.
        /// </summary>
        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        /// <summary>
        /// Called when a connection state changes for the local server.
        /// </summary>
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        /// <summary>
        /// Called when a connection state changes for a remote client.
        /// </summary>
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        /// <summary>
        /// Gets the current local ConnectionState.
        /// </summary>
        /// <param name="server">True if getting ConnectionState for the server.</param>
        public override LocalConnectionState GetConnectionState(bool server)
        {
            if (server)
                return this.server.GetLocalConnectionState();
            else
                return client.GetLocalConnectionState();
        }
        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            return server.GetConnectionState(connectionId);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for the local server.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
        }
        /// <summary>
        /// Handles a ConnectionStateArgs for a remote client.
        /// </summary>
        /// <param name="connectionStateArgs"></param>
        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }
        #endregion

        #region Iterating.
        /// <summary>
        /// Processes data received by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                this.server.IterateIncoming();

            }
            else
            {
                client.IterateIncoming();
                clientHost.IterateIncoming();
            }
        }

        /// <summary>
        /// Processes data to be sent by the socket.
        /// </summary>
        /// <param name="server">True to process data received on the server.</param>
        public override void IterateOutgoing(bool server)
        {
            if (server)
                this.server.IterateOutgoing();
            else
                client.IterateOutgoing();
        }
        #endregion

        #region ReceivedData.
        /// <summary>
        /// Called when client receives data.
        /// </summary>
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }
        /// <summary>
        /// Called when server receives data.
        /// </summary>
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;
        /// <summary>
        /// Handles a ClientReceivedDataArgs.
        /// </summary>
        /// <param name="receivedDataArgs"></param>
        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }
        #endregion

        #region Sending.
        /// <summary>
        /// Sends to the server or all clients.
        /// </summary>
        /// <param name="channelId">Channel to use.</param>
        /// /// <param name="segment">Data to send.</param>
        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            client.SendToServer(channelId, segment);
            clientHost.SendToServer(channelId, segment);
        }
        /// <summary>
        /// Sends data to a client.
        /// </summary>
        /// <param name="channelId"></param>
        /// <param name="segment"></param>
        /// <param name="connectionId"></param>
        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            server.SendToClient(channelId, segment, connectionId);
        }
        #endregion

        #region Configuration.
        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server. If the transport does not support this method the value -1 is returned.
        /// </summary>
        /// <returns></returns>
        public override int GetMaximumClients()
        {
            return server.GetMaximumClients();
        }
        /// <summary>
        /// Sets maximum number of clients allowed to connect to the server. If applied at runtime and clients exceed this value existing clients will stay connected but new clients may not connect.
        /// </summary>
        /// <param name="value"></param>
        public override void SetMaximumClients(int value)
        {
            server.SetMaximumClients(value);
        }
        /// <summary>
        /// Sets which address the client will connect to.
        /// </summary>
        /// <param name="address"></param>
        public override void SetClientAddress(string address)
        {
            clientAddress = address;
        }
        public override void SetServerBindAddress(string address, IPAddressType addressType)
        {
            serverBindAddress = address;
        }
        /// <summary>
        /// Sets which port to use.
        /// </summary>
        /// <param name="port"></param>
        public override void SetPort(ushort port)
        {
            this.port = port;
        }
        /// <summary>
        /// Returns the adjusted timeout as float
        /// </summary>
        /// <param name="asServer"></param>
        public override float GetTimeout(bool asServer)
        {
            return timeout;
        }
        #endregion

        #region Start and stop.
        /// <summary>
        /// Starts the local server or client using configured settings.
        /// </summary>
        /// <param name="server">True to start server.</param>
        public override bool StartConnection(bool server)
        {
            Debug.Log("StartConnection fishy server: " + server);

            if (server)
                return StartServer();
            else
                return StartClient(clientAddress);
        }

        /// <summary>
        /// Stops the local server or client.
        /// </summary>
        /// <param name="server">True to stop server.</param>
        public override bool StopConnection(bool server)
        {
            if (server)
                return StopServer();
            else
                return StopClient();
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId">ConnectionId of the client to disconnect.</param>
        /// <param name="immediately">True to abrutly stp the client socket without waiting socket thread.</param>
        public override bool StopConnection(int connectionId, bool immediately)
        {
            return StopClient(connectionId, immediately);
        }

        /// <summary>
        /// Stops both client and server.
        /// </summary>
        public override void Shutdown()
        {
            //Stops client then server connections.
            StopConnection(false);
            StopConnection(true);
        }

        #region Privates.
        /// <summary>
        /// Starts server.
        /// </summary>
        /// <returns>True if there were no blocks. A true response does not promise a socket will or has connected.</returns>
        private bool StartServer()
        {
            bool clientRunning = false;
#if !UNITY_SERVER
            if (!SteamClient.IsValid)
            {
                Debug.LogError("Steam Facepunch not initialized. Server could not be started.");
                return false;
            }
            //if (_client.GetLocalConnectionState() != LocalConnectionState.Stopped)
            //{
            //    Debug.LogError("Server cannot run while client is running.");
            //    return false;
            //}

            clientRunning = (client.GetLocalConnectionState() != LocalConnectionState.Stopped);
            /* If remote _client is running then stop it
             * and start the client host variant. */
            if (clientRunning)
                client.StopConnection();
#endif

            server.ResetInvalidSocket();
            if (server.GetLocalConnectionState() != LocalConnectionState.Stopped)
            {
                Debug.LogError("Server is already running.");
                return false;
            }
            InitializeRelayNetworkAccess();

            bool result = server.StartConnection(serverBindAddress, port, maximumClients);

            //If need to restart client.
            if (result && clientRunning)
                StartConnection(false);

            return result;
        }

        /// <summary>
        /// Stops server.
        /// </summary>
        private bool StopServer()
        {
            return server.StopConnection();
        }

        /// <summary>
        /// Starts the client.
        /// </summary>
        /// <param name="address"></param>
        /// <returns>True if there were no blocks. A true response does not promise a socket will or has connected.</returns>
        private bool StartClient(string address)
        {
            if (!SteamClient.IsValid)
            {
                Debug.LogError("Steam Facepunch not initialized. Client could not be started.");
                return false;
            }

            //If not acting as a host.
            if (server.GetLocalConnectionState() == LocalConnectionState.Stopped)
            {
                if (client.GetLocalConnectionState() != LocalConnectionState.Stopped)
                {
                    Debug.LogError("Client is already running.");
                    return false;
                }
                //Stop client host if running.
                if (clientHost.GetLocalConnectionState() != LocalConnectionState.Stopped)
                    clientHost.StopConnection();
                //Initialize.
                InitializeRelayNetworkAccess();

                client.StartConnection(address, port);
            }
            //Acting as host.
            else
            {
                clientHost.StartConnection(server);
            }

            return true;
        }

        /// <summary>
        /// Stops the client.
        /// </summary>
        private bool StopClient()
        {
            bool result = false;
            result |= client.StopConnection();
            result |= clientHost.StopConnection();
            return result;
        }

        /// <summary>
        /// Stops a remote client on the server.
        /// </summary>
        /// <param name="connectionId"></param>
        /// <param name="immediately">True to abrutly stp the client socket without waiting socket thread.</param>
        private bool StopClient(int connectionId, bool immediately)
        {
            return server.StopConnection(connectionId);
        }
        #endregion
        #endregion

        #region Channels.
        /// <summary>
        /// Gets the MTU for a channel. This should take header size into consideration.
        /// For example, if MTU is 1200 and a packet header for this channel is 10 in size, this method should return 1190.
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public override int GetMTU(byte channel)
        {
            if (channel >= mtus.Length)
            {
                Debug.LogError($"Channel {channel} is out of bounds.");
                return 0;
            }

            return mtus[channel];
        }
        #endregion
    }
}
#endif // !DISABLESTEAMWORKS