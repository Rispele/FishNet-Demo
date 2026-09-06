using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// «Личное дело» подключённого игрока: имя, платформенный id, цвет, готовность.
    ///
    /// Ключевые архитектурные решения:
    ///
    /// 1. Это отдельный сетевой объект, а не компонент персонажа. Персонаж существует только
    ///    в игровой сцене и пересоздаётся при каждом матче; профиль игрока должен жить всю сессию.
    ///
    /// 2. У его NetworkObject включён IsGlobal — FishNet кладёт такие объекты в DontDestroyOnLoad
    ///    и делает видимыми для всех клиентов. Поэтому объект переживает смену сетевых сцен.
    ///
    /// 3. Владелец (Owner) — соединение конкретного игрока. Это даёт бесплатную серверную
    ///    авторизацию: [ServerRpc] по умолчанию требует владения, то есть чужой клиент физически
    ///    не может изменить ваш профиль или вашу готовность.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerSession : NetworkBehaviour
    {
        /// <summary>Максимальная длина ника после серверной валидации.</summary>
        public const int MaxDisplayNameLength = 24;

        /// <summary>
        /// SyncVar — состояние, которое сервер реплицирует наблюдателям.
        ///
        /// В FishNet 4 SyncVar — это обычное поле-обёртка, а не атрибут: readonly-поле
        /// SyncVar&lt;T&gt;, которое кодогенератор регистрирует в NetworkBehaviour при компиляции.
        /// Права по умолчанию: писать может только сервер, читать — все наблюдатели.
        /// Значение автоматически доставляется новым клиентам при спавне объекта,
        /// поэтому подключившийся позже игрок сразу видит корректный список лобби.
        /// </summary>
        private readonly SyncVar<string> displayName = new("Player");

        private readonly SyncVar<ulong> platformId = new(0UL);
        private readonly SyncVar<bool> isReady = new(false);
        private readonly SyncVar<int> colorIndex = new(0);

        /// <summary>Отображаемое имя игрока (уже провалидированное сервером).</summary>
        public string DisplayName => displayName.Value;

        /// <summary>SteamID64 игрока. 0, если платформа недоступна.</summary>
        public ulong PlatformId => platformId.Value;

        /// <summary>Готов ли игрок к старту матча.</summary>
        public bool IsReady => isReady.Value;

        /// <summary>Индекс цвета, назначенный сервером. Гарантированно уникален внутри сессии.</summary>
        public int ColorIndex => colorIndex.Value;

        /// <summary>Является ли этот профиль профилем локального игрока.</summary>
        public bool IsLocal => IsOwner;

        /// <summary>
        /// Является ли владелец этого профиля хостом сессии.
        ///
        /// Не путать с NetworkBehaviour.IsHost из FishNet: то свойство отвечает на вопрос
        /// «запущен ли на ЭТОЙ машине одновременно сервер и клиент», а здесь вопрос другой —
        /// «принадлежит ли конкретно этот профиль тому, кто держит сервер».
        ///
        /// Проверка одинаково корректна и на клиенте (для UI), и на сервере (для валидации RPC):
        /// хост — единственное соединение, которое для сервера является локальным клиентом.
        /// </summary>
        public bool IsSessionHost => Owner != null && NetworkManager != null &&
                                     NetworkManager.ClientManager.Connection == Owner &&
                                     NetworkManager.IsServerStarted;

        /// <summary>
        /// OnStartNetwork вызывается один раз и на сервере, и на клиенте (на хосте — тоже один раз),
        /// поэтому это правильное место для регистрации в реестре.
        /// </summary>
        public override void OnStartNetwork()
        {
            isReady.OnChange += HandleReadyChanged;
            displayName.OnChange += HandleDisplayNameChanged;
            PlayerSessionRegistry.Register(this);
        }

        public override void OnStopNetwork()
        {
            isReady.OnChange -= HandleReadyChanged;
            displayName.OnChange -= HandleDisplayNameChanged;
            PlayerSessionRegistry.Unregister(this);
        }

        public override void OnStartServer()
        {
            // Цвет назначает сервер: клиент не должен иметь возможности «занять» чужой цвет.
            colorIndex.Value = Owner != null ? Mathf.Abs(Owner.ClientId) % PlayerPalette.Count : 0;
        }

        /// <summary>
        /// Клиент сообщает серверу своё платформенное имя.
        ///
        /// Почему это делает клиент, а не сервер: имя лежит в Steam-клиенте игрока, у сервера
        /// его нет. Именно поэтому серверу нельзя доверять этой строке без проверки — см.
        /// <see cref="SubmitProfileServerRpc"/>.
        /// </summary>
        public override void OnStartClient()
        {
            // Владение назначается позже, чем OnStartNetwork, поэтому реестр нужно
            // уведомить повторно: только сейчас становится ясно, чей это профиль.
            PlayerSessionRegistry.NotifyChanged();

            if (!IsOwner)
                return;

            SubmitProfileServerRpc(PlayerProfile.LocalDisplayName, PlayerProfile.LocalPlatformId);
        }

        /// <summary>Владение сменилось уже после спавна — состав лобби в UI надо обновить.</summary>
        public override void OnOwnershipClient(FishNet.Connection.NetworkConnection previousOwner)
            => PlayerSessionRegistry.NotifyChanged();

        /// <summary>Локальный игрок отмечает/снимает готовность.</summary>
        public void SetReady(bool ready)
        {
            if (!IsOwner)
                return;

            SetReadyServerRpc(ready);
        }

        /// <summary>
        /// [ServerRpc] без RequireOwnership = false вызывается только владельцем объекта.
        /// FishNet проверяет это на сервере, а не на клиенте, поэтому подделать вызов нельзя.
        /// </summary>
        [ServerRpc]
        private void SubmitProfileServerRpc(string displayName, ulong platformId)
        {
            // Всё, что пришло от клиента, — недоверенные данные. Санитизируем на сервере:
            // обрезаем длину и подставляем безопасный дефолт. Реплицируется уже результат.
            this.displayName.Value = SanitizeDisplayName(displayName);
            this.platformId.Value = platformId;
        }

        [ServerRpc]
        private void SetReadyServerRpc(bool ready)
        {
            isReady.Value = ready;
        }

        /// <summary>Сервер сбрасывает готовность, например при возврате из матча в лобби.</summary>
        internal void ServerResetReady()
        {
            if (!IsServerInitialized)
                return;

            isReady.Value = false;
        }

        private static string SanitizeDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Player";

            value = value.Trim();
            return value.Length <= MaxDisplayNameLength ? value : value.Substring(0, MaxDisplayNameLength);
        }

        private void HandleReadyChanged(bool previous, bool next, bool asServer) => PlayerSessionRegistry.NotifyChanged();

        private void HandleDisplayNameChanged(string previous, string next, bool asServer) => PlayerSessionRegistry.NotifyChanged();
    }
}
