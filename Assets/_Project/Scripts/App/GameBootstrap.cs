using Coop.Core;
using Coop.Core.Config;
using Coop.Core.Diagnostics;
using Coop.Core.Sessions;
using Coop.Networking;
using Coop.Steam;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.Serialization;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace Coop.App
{
    /// <summary>
    /// Composition root приложения — единственное место, где создаются и связываются сервисы.
    ///
    /// Порядок инициализации здесь не случаен и является ключевым для этого проекта:
    ///
    /// 1. Steam поднимается ПЕРВЫМ. Транспорт FishyFacepunch при своей инициализации сам
    ///    вызывает SteamClient.Init и бросает исключение, если Steam не запущен — это уронило бы
    ///    весь NetworkManager. Инициализируя Steam заранее, мы либо получаем рабочий Steam
    ///    (транспорт увидит SteamClient.IsValid и не будет делать ничего), либо узнаём о проблеме
    ///    до создания NetworkManager и уходим в LAN-режим.
    ///
    /// 2. NetworkManager создаётся из префаба, а не лежит в сцене. Это даёт нам контроль
    ///    над моментом его Awake: положив его в сцену, мы бы не смогли гарантировать,
    ///    что Steam инициализируется раньше.
    ///
    /// 3. Только потом создаётся SessionManager и публикуются сервисы для UI.
    ///
    /// Сама сцена Bootstrap после этого не нужна: все системы уходят в DontDestroyOnLoad,
    /// а сцена заменяется главным меню.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string LogContext = "Bootstrap";

        [FormerlySerializedAs("_config")]
        [Header("Configuration")]
        [SerializeField] private AppConfig config;

        [FormerlySerializedAs("_steamNetworkManagerPrefab")]
        [Header("Prefabs")]
        [Tooltip("NetworkManager с транспортом FishyFacepunch (Steam P2P).")]
        [SerializeField] private NetworkManager steamNetworkManagerPrefab;

        [FormerlySerializedAs("_lanNetworkManagerPrefab")]
        [Tooltip("NetworkManager с транспортом Tugboat (прямой IP). Используется, когда Steam недоступен.")]
        [SerializeField] private NetworkManager lanNetworkManagerPrefab;

        [FormerlySerializedAs("_sessionManagerPrefab")]
        [Tooltip("Префаб с SessionManager.")]
        [SerializeField] private SessionManager sessionManagerPrefab;

        private SteamService steam;
        private ILobbyService lobby;
        private CommandLineOptions commandLine;

        private void Awake()
        {
            // Защита от повторной инициализации, если Bootstrap случайно окажется загружен дважды.
            if (SessionServices.IsReady)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        /// <summary>
        /// Steam требует, чтобы очередь его колбэков прокачивалась каждый кадр.
        /// Делаем это в одном известном месте, а не в каждом сервисе.
        /// </summary>
        private void Update() => steam?.Tick();

        private void OnDestroy()
        {
            lobby?.Dispose();
            steam?.Dispose();
            SessionServices.Clear();
        }

        private void Initialize()
        {
            if (config == null)
            {
                CoopLog.Error(LogContext, "Не назначен AppConfig — запуск невозможен.", this);
                return;
            }

            commandLine = CommandLineOptions.Parse();

            bool steamMode = TryInitializeSteam(out string steamError);

            ILocalUser localUser = steamMode ? steam : new OfflineLocalUser();

            // Сетевой слой не должен знать про Steam, поэтому имя и id кладём в простой снимок.
            PlayerProfile.Set(localUser.DisplayName, localUser.Id);

            lobby = steamMode
                ? new SteamLobbyService(config.BuildId, localUser.DisplayName)
                : new LanLobbyService(config.LanAddress);

            NetworkManager networkManagerPrefab = steamMode ? steamNetworkManagerPrefab : lanNetworkManagerPrefab;
            if (networkManagerPrefab == null)
            {
                CoopLog.Error(LogContext, "Не назначен префаб NetworkManager.", this);
                return;
            }

            NetworkManager networkManager = Instantiate(networkManagerPrefab);
            networkManager.name = networkManagerPrefab.name;

            SessionManager session = Instantiate(sessionManagerPrefab);
            session.name = sessionManagerPrefab.name;
            DontDestroyOnLoad(session.gameObject);
            session.Construct(networkManager, lobby, localUser, config, steamMode);

            SessionServices.Register(session, lobby, localUser, config, steamMode, steamError);

            CoopLog.Info(LogContext, steamMode
                ? $"Режим Steam. Игрок: {localUser.DisplayName} ({localUser.Id})."
                : $"LAN-режим. Причина: {steamError}");

            UnitySceneManager.LoadScene(SceneCatalog.MainMenu);

            // Игру могли запустить по приглашению: Steam передаёт "+connect_lobby <id>".
            // Обрабатываем это после загрузки меню, чтобы пользователь видел состояние подключения.
            if (steamMode && SteamLaunchArguments.TryGetPendingLobby(out string lobbyKey))
            {
                CoopLog.Info(LogContext, $"Запуск по приглашению, подключаемся к лобби {lobbyKey}.");
                session.Join(lobbyKey);
                return;
            }

            ApplyCommandLine(session);
        }

        /// <summary>Автоматический хост/подключение по флагам командной строки (см. CommandLineOptions).</summary>
        private void ApplyCommandLine(SessionManager session)
        {
            if (commandLine.autoHost)
            {
                CoopLog.Info(LogContext, "Аргумент -autohost: создаём сессию автоматически.");
                session.Host();
                return;
            }

            if (commandLine.HasAutoJoin)
            {
                CoopLog.Info(LogContext, $"Аргумент -autojoin: подключаемся к '{commandLine.autoJoinAddress}'.");
                session.Join(commandLine.autoJoinAddress);
            }
        }

        private bool TryInitializeSteam(out string error)
        {
            error = string.Empty;

            if (config.ForceLanMode || commandLine.forceLan)
            {
                error = "Включён LAN-режим (AppConfig.ForceLanMode или аргумент -lan).";
                return false;
            }

            if (!SteamService.TryCreate(config.SteamAppId, out steam, out error))
                return false;

            return true;
        }

        /// <summary>
        /// Заглушка пользователя для LAN-режима.
        ///
        /// В имя добавлен id процесса: при локальном тестировании два экземпляра игры
        /// запускаются на одной машине, и одинаковые ники сделали бы лобби бесполезным.
        /// </summary>
        private sealed class OfflineLocalUser : ILocalUser
        {
            public bool IsValid => false;
            public ulong Id => 0UL;
            public string DisplayName { get; } =
                $"{SystemInfo.deviceName}-{System.Diagnostics.Process.GetCurrentProcess().Id}";
        }
    }
}
