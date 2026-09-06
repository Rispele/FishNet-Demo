using System;
using System.Threading.Tasks;
using Coop.Core;
using Coop.Core.Config;
using Coop.Core.Diagnostics;
using Coop.Core.Sessions;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.Serialization;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace Coop.Networking
{
    /// <summary>
    /// Оркестратор сессии: единственное место, где встречаются «матчмейкинг» и «транспорт».
    ///
    /// Ответственность:
    /// * поднять/остановить сервер и клиент FishNet;
    /// * связать адрес хоста из лобби с транспортом;
    /// * заспавнить <see cref="SessionCoordinator"/> и загрузить стартовую сетевую сцену;
    /// * корректно вернуть игру в главное меню при любом сценарии отключения.
    ///
    /// Чего он НЕ делает: не знает про Steam (только про <see cref="ILobbyService"/>),
    /// не знает про UI (UI подписывается на его события), не содержит игровой логики.
    ///
    /// Живёт в DontDestroyOnLoad, создаётся composition root'ом (GameBootstrap).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        private const string LogContext = "Session";

        /// <summary>Локальный адрес, по которому host-клиент подключается к собственному серверу.</summary>
        private const string LoopbackAddress = "127.0.0.1";

        [FormerlySerializedAs("_sessionCoordinatorPrefab")]
        [Tooltip("Префаб с SessionCoordinator. Спавнится сервером один раз за сессию.")]
        [SerializeField] private NetworkObject sessionCoordinatorPrefab;

        private NetworkManager networkManager;
        private ILobbyService lobby;
        private ILocalUser localUser;
        private AppConfig config;
        private bool useSteamAddressing;

        private string pendingJoinKey;
        private bool clientStartRequested;

        /// <summary>Текущее состояние сессии.</summary>
        public SessionState State { get; private set; } = SessionState.Offline;

        /// <summary>Сообщение о последней ошибке для показа в UI. Может быть пустым.</summary>
        public string LastError { get; private set; } = string.Empty;

        public event Action<SessionState> StateChanged;
        public event Action<string> Failed;

        /// <summary>Можно ли приглашать друзей прямо сейчас.</summary>
        public bool CanInvite => lobby != null && lobby.SupportsInvites && lobby.IsInLobby;

        /// <summary>
        /// Внедрение зависимостей вручную. В проекте без DI-контейнера это честнее, чем
        /// статические синглтоны: видно, что именно нужно объекту и кто это ему дал.
        /// </summary>
        /// <param name="useSteamAddressing">
        /// True, если активный транспорт адресуется SteamID64 (FishyFacepunch);
        /// false для IP-адресации (Tugboat).
        /// </param>
        public void Construct(NetworkManager networkManager, ILobbyService lobby, ILocalUser localUser,
            AppConfig config, bool useSteamAddressing)
        {
            this.networkManager = networkManager;
            this.lobby = lobby;
            this.localUser = localUser;
            this.config = config;
            this.useSteamAddressing = useSteamAddressing;

            this.networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            this.networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;

            this.lobby.HostAddressChanged += HandleHostAddressChanged;
            this.lobby.JoinRequested += HandleJoinRequested;
            this.lobby.Failed += HandleLobbyFailed;
        }

        private void OnDestroy()
        {
            if (networkManager != null)
            {
                networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
                networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
            }

            if (lobby != null)
            {
                lobby.HostAddressChanged -= HandleHostAddressChanged;
                lobby.JoinRequested -= HandleJoinRequested;
                lobby.Failed -= HandleLobbyFailed;
            }
        }

        #region Public API

        /// <summary>Создать лобби и поднять сервер. Точка входа для кнопки «Создать игру».</summary>
        public void Host() => RunGuarded(HostAsync());

        /// <summary>Войти в чужое лобби. Точка входа для приглашения и для LAN-подключения.</summary>
        public void Join(string lobbyKey) => RunGuarded(JoinAsync(lobbyKey));

        /// <summary>
        /// Открыть/закрыть лобби для новых участников. Вызывается сервером при старте матча.
        /// </summary>
        public void SetLobbyJoinable(bool joinable) => lobby?.SetJoinable(joinable);

        /// <summary>Пригласить друзей через оверлей платформы.</summary>
        public void InviteFriends() => lobby?.OpenInviteOverlay();

        /// <summary>
        /// Завершить сессию и вернуться в меню. Безопасно вызывать в любом состоянии,
        /// в том числе повторно.
        /// </summary>
        public void Leave(string reason = null)
        {
            if (State == SessionState.Offline)
                return;

            SetState(SessionState.Disconnecting);

            if (!string.IsNullOrEmpty(reason))
            {
                LastError = reason;
                Failed?.Invoke(reason);
            }

            // Порядок важен: сначала клиент, потом сервер. Так остальные участники
            // получают корректный disconnect вместо таймаута.
            if (networkManager.ClientManager.Started)
                networkManager.ClientManager.StopConnection();

            if (networkManager.ServerManager.Started)
                networkManager.ServerManager.StopConnection(sendDisconnectMessage: true);

            lobby.Leave();

            pendingJoinKey = null;
            clientStartRequested = false;

            SetState(SessionState.Offline);
            UnitySceneManager.LoadScene(SceneCatalog.MainMenu);
        }

        #endregion

        #region Host / Join

        private async Task HostAsync()
        {
            if (State != SessionState.Offline)
                return;

            LastError = string.Empty;
            SetState(SessionState.Hosting);

            // 1. Комната в матчмейкинге. Для LAN-режима это no-op.
            if (!await lobby.CreateAsync(config.MaxPlayers))
            {
                Abort("Не удалось создать лобби.");
                return;
            }

            // 2. Сервер. Порт и бинд берутся из настроек транспорта на префабе NetworkManager.
            if (!networkManager.ServerManager.StartConnection())
            {
                Abort("Не удалось запустить сервер.");
                return;
            }

            // 3. Публикуем адрес хоста. Клиенты прочитают его из метаданных лобби.
            //    Для Steam это SteamID64: FishyFacepunch устанавливает по нему P2P-соединение
            //    через Steam Datagram Relay, реальный IP хоста наружу не попадает.
            lobby.PublishHostAddress(HostAdvertisedAddress());

            // 4. Хост — это тот же процесс в роли клиента (listen server).
            //    FishyFacepunch в этом случае не идёт в сеть вовсе, а использует
            //    внутренний ClientHostSocket — трафик хоста не покидает процесс.
            StartClientConnection(LoopbackAddress);
        }

        private async Task JoinAsync(string lobbyKey)
        {
            if (State != SessionState.Offline)
                return;

            LastError = string.Empty;
            SetState(SessionState.Connecting);

            if (!await lobby.JoinAsync(lobbyKey))
            {
                Abort("Не удалось войти в лобби.");
                return;
            }

            // Адрес хоста может быть ещё не опубликован — тогда ждём HostAddressChanged.
            if (!string.IsNullOrEmpty(lobby.HostAddress))
                StartClientConnection(lobby.HostAddress);
            else
                CoopLog.Info(LogContext, "Ждём, пока хост опубликует адрес в метаданных лобби.");
        }

        /// <summary>
        /// Единственное место, где адрес из лобби попадает в транспорт.
        ///
        /// <see cref="Transport.SetClientAddress"/> — это часть базового контракта транспорта
        /// FishNet, поэтому здесь нет ни одной строчки, специфичной для FishyFacepunch или
        /// Tugboat. Именно это позволяет менять транспорт, не трогая логику сессии.
        /// </summary>
        private void StartClientConnection(string address)
        {
            if (clientStartRequested)
                return;

            clientStartRequested = true;

            CoopLog.Info(LogContext, $"Подключаемся к хосту по адресу '{address}'.");
            networkManager.ClientManager.StartConnection(address);
        }

        private string HostAdvertisedAddress()
        {
            if (useSteamAddressing)
                return localUser.Id.ToString();

            return string.IsNullOrEmpty(config.LanAddress) ? LoopbackAddress : config.LanAddress;
        }

        private void Abort(string reason)
        {
            LastError = reason;
            CoopLog.Warning(LogContext, reason);
            Failed?.Invoke(reason);

            lobby.Leave();
            clientStartRequested = false;

            if (networkManager.ServerManager.Started)
                networkManager.ServerManager.StopConnection(sendDisconnectMessage: false);

            SetState(SessionState.Offline);
        }

        #endregion

        #region FishNet callbacks

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
                return;

            SpawnSessionCoordinator();
            LoadNetworkScene(SceneCatalog.Lobby);
        }

        private void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    SetState(SessionState.Connected);
                    break;

                case LocalConnectionState.Stopped:
                    // Сюда попадаем и при штатном выходе, и при обрыве связи, и при кике.
                    if (State == SessionState.Connected || State == SessionState.Connecting)
                        Leave(State == SessionState.Connecting
                            ? "Не удалось подключиться к хосту."
                            : "Соединение с хостом потеряно.");
                    break;
            }
        }

        private void HandleHostAddressChanged(string address)
        {
            if (State != SessionState.Connecting || lobby.IsOwner)
                return;

            StartClientConnection(address);
        }

        private void HandleJoinRequested(string lobbyKey)
        {
            // Приглашение может прийти в любой момент, в том числе посреди другой сессии.
            if (State != SessionState.Offline)
            {
                CoopLog.Info(LogContext, "Приглашение получено во время сессии: выходим из текущей.");
                pendingJoinKey = lobbyKey;
                Leave();
                return;
            }

            Join(lobbyKey);
        }

        private void HandleLobbyFailed(string message)
        {
            LastError = message;
            Failed?.Invoke(message);
        }

        #endregion

        #region Server side session setup

        /// <summary>
        /// Спавнит сетевой объект-координатор сессии.
        ///
        /// У его NetworkObject включён флаг IsGlobal: FishNet переносит такие объекты
        /// в DontDestroyOnLoad и показывает их всем клиентам. Благодаря этому координатор
        /// переживает смену сетевых сцен Lobby → Game и остаётся единой точкой правды сервера.
        /// </summary>
        private void SpawnSessionCoordinator()
        {
            if (sessionCoordinatorPrefab == null)
            {
                CoopLog.Error(LogContext, "Не назначен префаб SessionCoordinator.", this);
                return;
            }

            NetworkObject instance = networkManager.GetPooledInstantiated(
                sessionCoordinatorPrefab, Vector3.zero, Quaternion.identity, asServer: true);

            networkManager.ServerManager.Spawn(instance);
        }

        /// <summary>
        /// Загружает сетевую сцену как «глобальную».
        ///
        /// Глобальная сцена — та, которая должна быть загружена у всех подключённых клиентов
        /// и у всех, кто подключится позже. FishNet сам:
        /// * рассылает команду загрузки клиентам,
        /// * ждёт их готовности,
        /// * выгружает у них старые сцены (ReplaceOption.All),
        /// * восстанавливает observers и спавнит объекты сцены.
        ///
        /// ReplaceOption.All выгружает и оффлайновые сцены (главное меню) — именно то,
        /// что нужно при переходе «меню → лобби». Системные объекты живут в DontDestroyOnLoad
        /// и выгрузку переживают.
        /// </summary>
        private void LoadNetworkScene(string sceneName)
        {
            SceneLoadData data = new(sceneName)
            {
                ReplaceScenes = ReplaceOption.All
            };

            networkManager.SceneManager.LoadGlobalScenes(data);
        }

        #endregion

        private void SetState(SessionState state)
        {
            if (State == state)
                return;

            State = state;
            CoopLog.Info(LogContext, $"Состояние сессии: {state}.");
            StateChanged?.Invoke(state);

            // Отложенное приглашение обрабатываем только после полного возврата в оффлайн.
            if (state == SessionState.Offline && !string.IsNullOrEmpty(pendingJoinKey))
            {
                string key = pendingJoinKey;
                pendingJoinKey = null;
                Join(key);
            }
        }

        /// <summary>
        /// Запускает Task «в фоне», но с обязательным логированием исключений.
        ///
        /// Голый async void опасен: необработанное исключение в нём уходит в никуда,
        /// и вместо понятной ошибки получаешь молча зависший UI.
        /// </summary>
        private async void RunGuarded(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                CoopLog.Exception(LogContext, exception, this);
                Abort($"Внутренняя ошибка: {exception.Message}");
            }
        }
    }
}
