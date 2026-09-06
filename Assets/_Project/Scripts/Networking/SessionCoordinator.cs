using System;
using System.Collections.Generic;
using Coop.Core;
using Coop.Core.Diagnostics;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Coop.Networking
{
    /// <summary>
    /// Серверный «мозг» сессии, реплицируемый клиентам.
    ///
    /// Отвечает за:
    /// * спавн профиля <see cref="PlayerSession"/> на каждое подключение;
    /// * переход лобби → матч (загрузка глобальной сетевой сцены);
    /// * спавн персонажей, когда клиент реально оказался в игровой сцене;
    /// * уборку за отключившимися игроками.
    ///
    /// Вся логика, меняющая состояние мира, выполняется ТОЛЬКО на сервере. Клиент может
    /// лишь попросить об изменении через [ServerRpc], а сервер решает, выполнять ли просьбу.
    /// Это и есть «server authoritative» модель, на которой построен FishNet.
    ///
    /// Объект помечен IsGlobal, поэтому переживает смену сетевых сцен и виден всем клиентам.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SessionCoordinator : NetworkBehaviour
    {
        private const string LogContext = "Coordinator";

        /// <summary>
        /// Экземпляр, доступный UI. Устанавливается в OnStartNetwork и обнуляется в OnStopNetwork,
        /// поэтому «висячей» ссылки после конца сессии не остаётся.
        /// </summary>
        public static SessionCoordinator Instance { get; private set; }

        /// <summary>Экземпляр появился или исчез.</summary>
        public static event Action<SessionCoordinator> InstanceChanged;

        [FormerlySerializedAs("_playerSessionPrefab")]
        [Header("Networked prefabs")]
        [Tooltip("Профиль игрока. Спавнится по одному на каждое подключение, IsGlobal = true.")]
        [SerializeField] private NetworkObject playerSessionPrefab;

        [FormerlySerializedAs("_playerCharacterPrefab")]
        [Tooltip("Персонаж игрока. Спавнится в игровой сцене на каждое подключение.")]
        [SerializeField] private NetworkObject playerCharacterPrefab;

        private readonly SyncVar<SessionPhase> phase = new(SessionPhase.Lobby);

        /// <summary>Соединения, для которых персонаж уже создан. Только на сервере.</summary>
        private readonly Dictionary<int, NetworkObject> characters = new();

        private int nextSpawnPointIndex;

        /// <summary>
        /// Локальный оркестратор сессии. Нужен только серверу и только для того, чтобы
        /// закрыть/открыть лобби. Ищется один раз: SessionManager живёт в DontDestroyOnLoad
        /// в единственном экземпляре, созданном composition root'ом.
        /// </summary>
        private SessionManager sessionManager;

        /// <summary>Текущая фаза сессии, одинаковая у всех участников.</summary>
        public SessionPhase Phase => phase.Value;

        /// <summary>Фаза изменилась (для UI).</summary>
        public event Action<SessionPhase> PhaseChanged;

        public override void OnStartNetwork()
        {
            Instance = this;
            phase.OnChange += HandlePhaseChanged;
            InstanceChanged?.Invoke(this);
        }

        public override void OnStopNetwork()
        {
            phase.OnChange -= HandlePhaseChanged;

            if (Instance == this)
            {
                Instance = null;
                InstanceChanged?.Invoke(null);
            }
        }

        public override void OnStartServer()
        {
            sessionManager = FindAnyObjectByType<SessionManager>();

            // OnClientLoadedStartScenes — момент, когда клиент догрузил все сцены, выданные
            // ему при подключении, и готов принимать сетевые объекты. Спавнить раньше нельзя:
            // объект уйдёт клиенту, у которого ещё нет нужной сцены.
            SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;

            // OnClientPresenceChangeEnd — клиент добавлен в конкретную сцену (или убран из неё).
            // Это единственный надёжный триггер для спавна персонажа: он одинаково корректно
            // отрабатывает и для тех, кто был в лобби, и для тех, кто подключился в середине матча.
            SceneManager.OnClientPresenceChangeEnd += HandleClientPresenceChangeEnd;

            ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
        }

        public override void OnStopServer()
        {
            SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
            SceneManager.OnClientPresenceChangeEnd -= HandleClientPresenceChangeEnd;
            ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;

            characters.Clear();
        }

        #region Client requests

        /// <summary>
        /// Просьба клиента начать матч.
        ///
        /// RequireOwnership = false, потому что у координатора нет владельца-игрока.
        /// Взамен мы обязаны валидировать отправителя вручную: параметр NetworkConnection
        /// с значением по умолчанию FishNet подставляет сам, подделать его клиент не может.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestStartMatchServerRpc(NetworkConnection sender = null)
        {
            if (phase.Value != SessionPhase.Lobby)
                return;

            if (!IsHostConnection(sender))
            {
                CoopLog.Warning(LogContext, $"Клиент {sender?.ClientId} попытался начать матч, не будучи хостом.");
                return;
            }

            if (!PlayerSessionRegistry.AllReady())
            {
                CoopLog.Info(LogContext, "Старт отклонён: не все игроки готовы.");
                return;
            }

            StartMatch();
        }

        /// <summary>Просьба клиента вернуть сессию в лобби.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestReturnToLobbyServerRpc(NetworkConnection sender = null)
        {
            if (phase.Value != SessionPhase.Playing || !IsHostConnection(sender))
                return;

            ReturnToLobby();
        }

        #endregion

        #region Server flow

        private void StartMatch()
        {
            CoopLog.Info(LogContext, "Старт матча: загружаем игровую сцену.");

            phase.Value = SessionPhase.Playing;

            // Закрываем лобби от новых участников на время загрузки, чтобы не ловить
            // подключение ровно в момент смены сцен.
            sessionManager?.SetLobbyJoinable(false);

            LoadGlobalScene(SceneCatalog.Game);
        }

        private void ReturnToLobby()
        {
            CoopLog.Info(LogContext, "Возврат в лобби.");

            phase.Value = SessionPhase.Lobby;
            characters.Clear();
            nextSpawnPointIndex = 0;

            for (int i = 0; i < PlayerSessionRegistry.All.Count; i++)
                PlayerSessionRegistry.All[i].ServerResetReady();

            sessionManager?.SetLobbyJoinable(true);
            LoadGlobalScene(SceneCatalog.Lobby);
        }

        /// <summary>
        /// Загружает сцену как глобальную, заменяя все предыдущие.
        ///
        /// FishNet сам синхронизирует загрузку: сервер ждёт, пока каждый клиент отчитается
        /// о готовности, и только потом восстанавливает observers. Никакой ручной
        /// синхронизации «все загрузились» писать не нужно.
        /// </summary>
        private void LoadGlobalScene(string sceneName)
        {
            SceneLoadData data = new(sceneName)
            {
                ReplaceScenes = ReplaceOption.All
            };

            SceneManager.LoadGlobalScenes(data);
        }

        private void HandleClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
                return;

            // Один профиль на соединение, даже если клиент переподключился по сцене.
            if (PlayerSessionRegistry.FindByClientId(connection.ClientId) != null)
                return;

            if (playerSessionPrefab == null)
            {
                CoopLog.Error(LogContext, "Не назначен префаб PlayerSession.", this);
                return;
            }

            NetworkObject session = NetworkManager.GetPooledInstantiated(
                playerSessionPrefab, Vector3.zero, Quaternion.identity, asServer: true);

            // Передавая connection владельцем, мы разрешаем именно этому клиенту слать
            // [ServerRpc] на объект — остальным FishNet откажет на сервере.
            ServerManager.Spawn(session, connection);
        }

        private void HandleClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            if (!args.Added || args.Scene.name != SceneCatalog.Game)
                return;

            SpawnCharacter(args.Connection, args.Scene);
        }

        private void HandleRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            // FishNet сам деспавнит объекты, которыми владело отключившееся соединение,
            // но словарь принадлежит нам, и чистить его — наша обязанность.
            characters.Remove(connection.ClientId);
        }

        private void SpawnCharacter(NetworkConnection connection, Scene scene)
        {
            if (playerCharacterPrefab == null)
            {
                CoopLog.Error(LogContext, "Не назначен префаб персонажа.", this);
                return;
            }

            if (characters.TryGetValue(connection.ClientId, out NetworkObject existing) && existing != null && existing.IsSpawned)
                return;

            GetSpawnPose(scene, out Vector3 position, out Quaternion rotation);

            NetworkObject character = NetworkManager.GetPooledInstantiated(
                playerCharacterPrefab, position, rotation, asServer: true);

            // Третий аргумент кладёт объект именно в игровую сцену. Это важно: когда сцена
            // будет выгружена, FishNet деспавнит персонажей автоматически, и нам не нужно
            // писать отдельную уборку при возврате в лобби.
            ServerManager.Spawn(character, connection, scene);

            characters[connection.ClientId] = character;
            CoopLog.Info(LogContext, $"Персонаж создан для клиента {connection.ClientId}.");
        }

        /// <summary>
        /// Ищет точки спавна в загруженной игровой сцене и выдаёт их по кругу.
        /// Если точек нет — используем позицию префаба, чтобы демо всё равно запустилось.
        /// </summary>
        private void GetSpawnPose(Scene scene, out Vector3 position, out Quaternion rotation)
        {
            position = playerCharacterPrefab.transform.position;
            rotation = playerCharacterPrefab.transform.rotation;

            if (!scene.IsValid())
                return;

            List<PlayerSpawnPoint> points = new();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                points.AddRange(roots[i].GetComponentsInChildren<PlayerSpawnPoint>(includeInactive: false));

            if (points.Count == 0)
                return;

            PlayerSpawnPoint point = points[nextSpawnPointIndex % points.Count];
            nextSpawnPointIndex++;

            position = point.transform.position;
            rotation = point.transform.rotation;
        }

        #endregion

        /// <summary>
        /// Хост — единственное соединение, которое одновременно является локальным клиентом сервера.
        /// </summary>
        private bool IsHostConnection(NetworkConnection connection)
            => connection != null && NetworkManager.IsServerStarted && NetworkManager.ClientManager.Connection == connection;

        private void HandlePhaseChanged(SessionPhase previous, SessionPhase next, bool asServer)
        {
            if (asServer && NetworkManager.IsHostStarted)
                return; // На хосте событие придёт вторым вызовом (asServer = false), чтобы не дублировать.

            PhaseChanged?.Invoke(next);
        }
    }
}
