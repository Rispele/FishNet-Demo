using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// «Личное дело» подключённого игрока: имя, платформенный id, цвет, готовность.
    ///
    /// ───────── ГДЕ ЧТО ВЫПОЛНЯЕТСЯ ─────────
    ///
    /// Класс один, но его экземпляры живут в разных процессах и играют там разные роли:
    ///
    ///   на СЕРВЕРЕ            — единственная копия, которая имеет право писать в SyncVar;
    ///   у КЛИЕНТА-ВЛАДЕЛЬЦА   — копия игрока, которому этот профиль принадлежит (IsOwner);
    ///   у ОСТАЛЬНЫХ КЛИЕНТОВ  — копия только на чтение, обновляется репликацией.
    ///
    /// На хосте сервер и клиент — один процесс, поэтому там одна и та же копия играет
    /// сразу две роли. Именно это чаще всего и путает: код выглядит «выполняется дважды»,
    /// хотя на самом деле объект просто одновременно и серверный, и клиентский.
    ///
    /// Ниже методы сгруппированы по ролям, и у каждой группы указано, кто её выполняет.
    ///
    /// ───────── АРХИТЕКТУРНЫЕ РЕШЕНИЯ ─────────
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

        #region Реплицируемое состояние (пишет сервер, читают все)

        /// <summary>
        /// SyncVar — состояние, которое сервер реплицирует наблюдателям.
        ///
        /// В FishNet 4 SyncVar — это обычное поле-обёртка, а не атрибут: readonly-поле
        /// SyncVar&lt;T&gt;, которое кодогенератор регистрирует в NetworkBehaviour при компиляции.
        /// Права по умолчанию: писать может только сервер, читать — все наблюдатели.
        /// Значение автоматически доставляется новым клиентам при спавне объекта,
        /// поэтому подключившийся позже игрок сразу видит корректный список лобби.
        ///
        /// Присваивание `.Value` вне сервера просто не разойдётся по сети — это и есть
        /// физическая граница между «сервер решает» и «клиент отображает».
        /// </summary>
        private readonly SyncVar<string> displayName = new("Player");

        private readonly SyncVar<ulong> platformId = new(0UL);
        private readonly SyncVar<bool> isReady = new(false);
        private readonly SyncVar<int> colorIndex = new(0);

        #endregion

        #region Чтение состояния (доступно везде)

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

        #endregion

        #region ВЕЗДЕ: жизненный цикл объекта

        /// <summary>
        /// Выполняется: и на сервере, и на клиенте (на хосте — один раз).
        ///
        /// Это самая ранняя точка, где объект уже сетевой. Подходит для того, что нужно
        /// одинаково всем: подписки на SyncVar и регистрация в реестре.
        /// </summary>
        public override void OnStartNetwork()
        {
            isReady.OnChange += HandleReadyChanged;
            displayName.OnChange += HandleDisplayNameChanged;
            PlayerSessionRegistry.Register(this);
        }

        /// <summary>Выполняется: и на сервере, и на клиенте. Симметрично OnStartNetwork.</summary>
        public override void OnStopNetwork()
        {
            isReady.OnChange -= HandleReadyChanged;
            displayName.OnChange -= HandleDisplayNameChanged;
            PlayerSessionRegistry.Unregister(this);
        }

        #endregion

        #region ТОЛЬКО СЕРВЕР

        /// <summary>
        /// Выполняется: только на сервере (на хосте — тоже, он ведь сервер).
        ///
        /// Цвет назначает сервер: клиент не должен иметь возможности «занять» чужой цвет.
        /// </summary>
        public override void OnStartServer()
        {
            colorIndex.Value = Owner != null ? Mathf.Abs(Owner.ClientId) % PlayerPalette.Count : 0;
        }

        /// <summary>
        /// Выполняется: только на сервере. Вызывается серверным кодом напрямую
        /// (<see cref="SessionCoordinator"/> при возврате матча в лобби), не по сети.
        /// </summary>
        internal void ServerResetReady()
        {
            if (!IsServerInitialized)
                return;

            isReady.Value = false;
        }

        /// <summary>
        /// ВЫЗЫВАЕТСЯ у клиента-владельца, ВЫПОЛНЯЕТСЯ на сервере.
        ///
        /// Это и есть главный источник путаницы в сетевом коде: метод написан один раз,
        /// но точка вызова и точка исполнения — разные машины. Кодогенератор FishNet
        /// подменяет тело на «сериализовать аргументы и отправить», а на сервере
        /// разворачивает обратно и вызывает то, что написано ниже.
        ///
        /// [ServerRpc] без RequireOwnership = false принимается только от владельца объекта,
        /// и проверяет это сервер — подделать вызов с чужого клиента нельзя.
        /// </summary>
        [ServerRpc]
        private void SubmitProfileServerRpc(string displayName, ulong platformId)
        {
            // Всё, что пришло от клиента, — недоверенные данные. Санитизируем на сервере:
            // обрезаем длину и подставляем безопасный дефолт. Реплицируется уже результат.
            this.displayName.Value = SanitizeDisplayName(displayName);
            this.platformId.Value = platformId;
        }

        /// <summary>ВЫЗЫВАЕТСЯ у клиента-владельца, ВЫПОЛНЯЕТСЯ на сервере.</summary>
        [ServerRpc]
        private void SetReadyServerRpc(bool ready)
        {
            isReady.Value = ready;
        }

        private static string SanitizeDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Player";

            value = value.Trim();
            return value.Length <= MaxDisplayNameLength ? value : value.Substring(0, MaxDisplayNameLength);
        }

        #endregion

        #region ТОЛЬКО КЛИЕНТ

        /// <summary>
        /// Выполняется: на каждом клиенте, который видит этот объект — включая чужие профили.
        /// Тело метода само разделяется на «общую» часть и часть только для владельца.
        ///
        /// Ник берётся из Steam-клиента игрока, у сервера его нет, поэтому отправить профиль
        /// может только владелец. Именно поэтому сервер обязан не доверять этой строке —
        /// см. <see cref="SubmitProfileServerRpc"/>.
        /// </summary>
        public override void OnStartClient()
        {
            // Общая часть: владение назначается позже, чем OnStartNetwork, поэтому реестр
            // нужно уведомить повторно — только сейчас становится ясно, чей это профиль.
            PlayerSessionRegistry.NotifyChanged();

            // Дальше — только владелец.
            if (!IsOwner)
                return;

            SubmitProfileServerRpc(PlayerProfile.LocalDisplayName, PlayerProfile.LocalPlatformId);
        }

        /// <summary>
        /// Выполняется: на клиентах. Владение сменилось уже после спавна —
        /// состав лобби в UI надо обновить.
        /// </summary>
        public override void OnOwnershipClient(NetworkConnection previousOwner)
            => PlayerSessionRegistry.NotifyChanged();

        /// <summary>
        /// Выполняется: у клиента-владельца. Точка входа из UI лобби.
        ///
        /// Обратите внимание: метод ничего не меняет локально. Клиент не переключает
        /// свою готовность сам — он отправляет просьбу, а видимое значение приедет
        /// обратно репликацией SyncVar. Это делает состояние гарантированно одинаковым
        /// у всех и убирает «мигание» кнопки при отказе сервера.
        /// </summary>
        public void SetReady(bool ready)
        {
            if (!IsOwner)
                return;

            SetReadyServerRpc(ready);
        }

        #endregion

        #region ВЕЗДЕ: реакция на репликацию

        /* OnChange у SyncVar вызывается и на сервере (asServer = true), и на клиенте
         * (asServer = false). На хосте, соответственно, оба раза. Нам здесь всё равно:
         * реестр только просит UI перерисоваться, и повторный вызов безвреден. */

        private void HandleReadyChanged(bool previous, bool next, bool asServer)
            => PlayerSessionRegistry.NotifyChanged();

        private void HandleDisplayNameChanged(string previous, string next, bool asServer)
            => PlayerSessionRegistry.NotifyChanged();

        #endregion
    }
}
