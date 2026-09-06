using System;
using Coop.Core.Diagnostics;
using Coop.Core.Sessions;
using Steamworks;

namespace Coop.Steam
{
    /// <summary>
    /// Владелец жизненного цикла Steam API.
    ///
    /// Что делает Facepunch.Steamworks под капотом:
    /// * <see cref="SteamClient.Init"/> загружает steam_api64.dll, связывается с запущенным
    ///   клиентом Steam через IPC и отдаёт указатели на интерфейсы (ISteamUser, ISteamFriends,
    ///   ISteamMatchmaking, ISteamNetworkingSockets и т.д.);
    /// * все асинхронные ответы Steam приходят не сразу, а складываются во внутреннюю очередь.
    ///   Её нужно «прокачивать» — этим занимается <see cref="Tick"/>.
    ///
    /// Мы намеренно инициализируем Steam с asyncCallbacks: false и качаем очередь вручную из
    /// Update. Так все колбэки Steam гарантированно приходят в главном потоке Unity между
    /// кадрами — это делает поведение детерминированным и позволяет спокойно трогать
    /// UnityEngine API прямо в обработчиках.
    ///
    /// ВАЖНО: сервис создаётся composition root'ом ДО NetworkManager. Транспорт FishyFacepunch
    /// в своём Initialize() сам вызывает SteamClient.Init, если Steam ещё не поднят, и падает
    /// исключением, когда Steam не запущен. Инициализируя Steam первыми, мы контролируем
    /// эту ошибку и можем корректно уйти в LAN-режим вместо краша NetworkManager.
    /// </summary>
    public sealed class SteamService : ILocalUser, IDisposable
    {
        private const string LogContext = "Steam";

        private bool ownsSteamClient;
        private bool disposed;

        private SteamService(uint appId, bool ownsSteamClient)
        {
            AppId = appId;
            this.ownsSteamClient = ownsSteamClient;
        }

        /// <summary>AppId, с которым инициализирован Steam.</summary>
        public uint AppId { get; }

        public bool IsValid => !disposed && SteamClient.IsValid;

        public ulong Id => IsValid ? SteamClient.SteamId.Value : 0UL;

        public string DisplayName => IsValid ? SteamClient.Name : "Offline Player";

        /// <summary>
        /// Пытается поднять Steam API. Никогда не бросает исключений: недоступность Steam —
        /// это штатная ситуация (Steam закрыт, оффлайн-режим, запуск на билд-агенте),
        /// а не ошибка программиста.
        /// </summary>
        public static bool TryCreate(uint appId, out SteamService service, out string error)
        {
            service = null;
            error = null;

            // Домен мог не перезагружаться между входами в Play Mode (Enter Play Mode Options).
            // В этом случае Steam уже поднят, и повторный Init бросит исключение.
            if (SteamClient.IsValid)
            {
                CoopLog.Info(LogContext, "SteamClient уже инициализирован, переиспользуем его.");
                service = new SteamService(appId, ownsSteamClient: false);
                return true;
            }

            try
            {
                SteamClient.Init(appId, asyncCallbacks: false);
            }
            catch (Exception exception)
            {
                error = $"Не удалось инициализировать Steam (AppId {appId}): {exception.Message}. " +
                        "Убедитесь, что клиент Steam запущен и рядом с проектом лежит steam_appid.txt.";
                CoopLog.Warning(LogContext, error);
                return false;
            }

            if (!SteamClient.IsValid)
            {
                error = "SteamClient.Init отработал, но интерфейсы Steam недоступны.";
                CoopLog.Warning(LogContext, error);
                return false;
            }

            service = new SteamService(appId, ownsSteamClient: true);
            CoopLog.Info(LogContext, $"Steam готов: {service.DisplayName} ({service.Id}).");
            return true;
        }

        /// <summary>
        /// Прокачивает очередь колбэков Steam. Вызывать ровно раз за кадр.
        /// Без этого не придут ни OnLobbyCreated, ни OnGameLobbyJoinRequested, ни приглашения.
        /// </summary>
        public void Tick()
        {
            if (!IsValid)
                return;

            try
            {
                SteamClient.RunCallbacks();
            }
            catch (Exception exception)
            {
                // Исключение внутри пользовательского колбэка не должно ронять игровой цикл.
                CoopLog.Exception(LogContext, exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            if (!ownsSteamClient)
                return;

            ownsSteamClient = false;
            SteamClient.Shutdown();
            CoopLog.Info(LogContext, "SteamClient выключен.");
        }
    }
}
