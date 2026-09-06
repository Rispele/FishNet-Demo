using System;
using Coop.Core.Config;
using Coop.Core.Sessions;
using Coop.Networking;
using UnityEngine;

namespace Coop.App
{
    /// <summary>
    /// Точка доступа к сервисам приложения для объектов, которые нельзя сконструировать вручную.
    ///
    /// Почему это не «плохой синглтон»:
    /// * записывать сюда может только composition root (<see cref="GameBootstrap"/>) — метод
    ///   <see cref="Register"/> внутренний для сборки Coop.App;
    /// * сервисы здесь не создаются, а только публикуются: локатор не владеет их жизненным циклом;
    /// * это единственная статическая точка во всём приложении, и нужна она исключительно UI,
    ///   который живёт в отдельных сценах и физически не может получить ссылки конструктором.
    ///
    /// Альтернатива — полноценный DI-контейнер (VContainer / Zenject). Для учебного проекта
    /// это лишняя сущность, поэтому здесь явно ограниченный локатор.
    /// </summary>
    public static class SessionServices
    {
        public static SessionManager Session { get; private set; }
        public static ILobbyService Lobby { get; private set; }
        public static ILocalUser LocalUser { get; private set; }
        public static AppConfig Config { get; private set; }

        /// <summary>True, если работа идёт через Steam; false — LAN-режим без Steam.</summary>
        public static bool SteamMode { get; private set; }

        /// <summary>Сообщение о причине перехода в LAN-режим. Пустое, если Steam доступен.</summary>
        public static string SteamUnavailableReason { get; private set; } = string.Empty;

        public static bool IsReady => Session != null;

        /// <summary>Сервисы стали доступны. UI, загруженный позже, просто проверяет IsReady.</summary>
        public static event Action Ready;

        internal static void Register(SessionManager session, ILobbyService lobby, ILocalUser localUser,
            AppConfig config, bool steamMode, string steamUnavailableReason)
        {
            Session = session;
            Lobby = lobby;
            LocalUser = localUser;
            Config = config;
            SteamMode = steamMode;
            SteamUnavailableReason = steamUnavailableReason ?? string.Empty;

            Ready?.Invoke();
        }

        internal static void Clear()
        {
            Session = null;
            Lobby = null;
            LocalUser = null;
            Config = null;
            SteamMode = false;
            SteamUnavailableReason = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Clear();
            Ready = null;
        }
    }
}
