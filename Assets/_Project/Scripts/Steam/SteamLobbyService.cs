using System;
using System.Threading.Tasks;
using Coop.Core.Diagnostics;
using Coop.Core.Sessions;
using Steamworks;
using Steamworks.Data;

namespace Coop.Steam
{
    /// <summary>
    /// Реализация <see cref="ILobbyService"/> поверх Steam Matchmaking.
    ///
    /// Что такое Steam-лобби: это лёгкая «комната» на серверах Valve, у которой есть
    /// владелец, список участников и словарь строковых метаданных. Лобби НЕ передаёт
    /// игровой трафик — это исключительно механизм поиска друг друга, приглашений
    /// и синхронизации небольшого объёма данных до старта матча.
    ///
    /// Мы используем метаданные лобби ровно для двух вещей:
    /// * "coop_build"  — версия сборки, чтобы отсечь несовместимые клиенты;
    /// * "coop_host"   — SteamID64 хоста, то есть адрес для транспорта FishyFacepunch.
    ///
    /// Дальше в дело вступает FishNet: клиент берёт "coop_host" и подключается по нему
    /// через Steam Datagram Relay. Лобби к этому моменту уже сделало свою работу.
    /// </summary>
    public sealed class SteamLobbyService : ILobbyService
    {
        private const string LogContext = "SteamLobby";
        private const string BuildIdKey = "coop_build";
        private const string HostAddressKey = "coop_host";
        private const string HostNameKey = "coop_host_name";

        private readonly string buildId;
        private readonly string localDisplayName;

        private Lobby? lobby;
        private string hostAddress = string.Empty;
        private bool disposed;

        public SteamLobbyService(string buildId, string localDisplayName)
        {
            this.buildId = buildId ?? string.Empty;
            this.localDisplayName = localDisplayName ?? "Player";

            // События Facepunch статические, поэтому подписка обязана иметь симметричную
            // отписку в Dispose. Иначе после выхода из Play Mode в редакторе останется
            // «мёртвая» подписка на объект прошлой сессии — классическая утечка,
            // которая проявляется как двойная обработка приглашений.
            SteamMatchmaking.OnLobbyCreated += HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered += HandleLobbyEntered;
            SteamMatchmaking.OnLobbyDataChanged += HandleLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberJoined += HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += HandleLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected += HandleLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested += HandleGameLobbyJoinRequested;
        }

        public bool IsAvailable => !disposed && SteamClient.IsValid;

        public bool IsInLobby => lobby.HasValue;

        public bool IsOwner => lobby.HasValue && SteamClient.IsValid && lobby.Value.IsOwnedBy(SteamClient.SteamId);

        public bool SupportsInvites => true;

        public string HostAddress => hostAddress;

        public string LobbyKey => lobby.HasValue ? lobby.Value.Id.Value.ToString() : string.Empty;

        public event Action Entered;
        public event Action Exited;
        public event Action MembersChanged;
        public event Action<string> HostAddressChanged;
        public event Action<string> JoinRequested;
        public event Action<string> Failed;

        /// <summary>
        /// Создаёт лобби «только для друзей».
        ///
        /// Порядок важен: сначала публикуем метаданные, и только потом разрешаем вход.
        /// Иначе быстрый клиент успеет войти раньше, чем увидит "coop_host", и будет
        /// вынужден ждать события OnLobbyDataChanged.
        /// </summary>
        public async Task<bool> CreateAsync(int maxMembers)
        {
            if (!IsAvailable)
            {
                Failed?.Invoke("Steam недоступен.");
                return false;
            }

            Lobby? created = await SteamMatchmaking.CreateLobbyAsync(maxMembers);
            if (created == null)
            {
                Failed?.Invoke("Steam не смог создать лобби.");
                return false;
            }

            Lobby lobby = created.Value;
            this.lobby = lobby;

            // FriendsOnly: лобби видно только друзьям владельца и приглашённым.
            // Для публичного матчмейкинга здесь был бы SetPublic() + LobbyList-запросы.
            lobby.SetFriendsOnly();
            lobby.SetJoinable(true);
            lobby.SetData(BuildIdKey, buildId);
            lobby.SetData(HostNameKey, localDisplayName);

            CoopLog.Info(LogContext, $"Лобби создано: {lobby.Id.Value}, мест: {maxMembers}.");
            return true;
        }

        public async Task<bool> JoinAsync(string lobbyKey)
        {
            if (!IsAvailable)
            {
                Failed?.Invoke("Steam недоступен.");
                return false;
            }

            if (!ulong.TryParse(lobbyKey, out ulong lobbyId) || lobbyId == 0UL)
            {
                Failed?.Invoke($"Некорректный идентификатор лобби: '{lobbyKey}'.");
                return false;
            }

            // Повторный вход в то же лобби — не ошибка, просто ничего не делаем.
            if (lobby.HasValue && lobby.Value.Id.Value == lobbyId)
                return true;

            if (lobby.HasValue)
                Leave();

            Lobby? joined = await SteamMatchmaking.JoinLobbyAsync(lobbyId);
            if (joined == null)
            {
                Failed?.Invoke("Не удалось войти в лобби: оно закрыто, заполнено или больше не существует.");
                return false;
            }

            lobby = joined.Value;
            CoopLog.Info(LogContext, $"Вошли в лобби {lobbyId}.");
            return true;
        }

        public void Leave()
        {
            if (!lobby.HasValue)
                return;

            ulong id = lobby.Value.Id.Value;
            lobby.Value.Leave();
            lobby = null;
            SetHostAddress(string.Empty);

            CoopLog.Info(LogContext, $"Вышли из лобби {id}.");
            Exited?.Invoke();
        }

        public void PublishHostAddress(string address)
        {
            if (!lobby.HasValue)
                return;

            if (!IsOwner)
            {
                CoopLog.Warning(LogContext, "Публиковать адрес хоста может только владелец лобби.");
                return;
            }

            lobby.Value.SetData(HostAddressKey, address ?? string.Empty);
            SetHostAddress(address);
        }

        public void OpenInviteOverlay()
        {
            if (!lobby.HasValue)
            {
                Failed?.Invoke("Нельзя приглашать друзей: вы не в лобби.");
                return;
            }

            // Открывает нативный оверлей Steam со списком друзей. Само приглашение
            // отправляет Steam — игра не имеет доступа к списку контактов пользователя.
            SteamFriends.OpenGameInviteOverlay(lobby.Value.Id);
        }

        public void SetJoinable(bool joinable)
        {
            if (!lobby.HasValue || !IsOwner)
                return;

            lobby.Value.SetJoinable(joinable);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            SteamMatchmaking.OnLobbyCreated -= HandleLobbyCreated;
            SteamMatchmaking.OnLobbyEntered -= HandleLobbyEntered;
            SteamMatchmaking.OnLobbyDataChanged -= HandleLobbyDataChanged;
            SteamMatchmaking.OnLobbyMemberJoined -= HandleLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave -= HandleLobbyMemberLeave;
            SteamMatchmaking.OnLobbyMemberDisconnected -= HandleLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested -= HandleGameLobbyJoinRequested;

            Leave();
        }

        #region Steam callbacks

        private void HandleLobbyCreated(Result result, Lobby lobby)
        {
            if (result != Result.OK)
                Failed?.Invoke($"Steam вернул ошибку при создании лобби: {result}.");
        }

        /// <summary>
        /// Вызывается и у владельца, и у присоединившегося клиента.
        /// </summary>
        private void HandleLobbyEntered(Lobby lobby)
        {
            this.lobby = lobby;

            if (!IsOwner && !IsBuildCompatible(lobby))
            {
                Failed?.Invoke("Версия сборки хоста не совпадает с вашей.");
                Leave();
                return;
            }

            Entered?.Invoke();
            MembersChanged?.Invoke();
            TryReadHostAddress(lobby);
        }

        private void HandleLobbyDataChanged(Lobby lobby)
        {
            if (!IsSameLobby(lobby))
                return;

            this.lobby = lobby;
            TryReadHostAddress(lobby);
        }

        private void HandleLobbyMemberJoined(Lobby lobby, Friend friend)
        {
            if (!IsSameLobby(lobby))
                return;

            CoopLog.Info(LogContext, $"В лобби вошёл {friend.Name}.");
            MembersChanged?.Invoke();
        }

        private void HandleLobbyMemberLeave(Lobby lobby, Friend friend)
        {
            if (!IsSameLobby(lobby))
                return;

            CoopLog.Info(LogContext, $"Из лобби вышел {friend.Name}.");
            MembersChanged?.Invoke();
        }

        /// <summary>
        /// Пользователь нажал «Присоединиться» в оверлее/списке друзей, когда игра уже запущена.
        /// Случай «игра ещё не запущена» обрабатывает <see cref="SteamLaunchArguments"/>.
        /// </summary>
        private void HandleGameLobbyJoinRequested(Lobby lobby, SteamId invitedBy)
        {
            CoopLog.Info(LogContext, $"Запрошен вход в лобби {lobby.Id.Value} (пригласил {invitedBy.Value}).");
            JoinRequested?.Invoke(lobby.Id.Value.ToString());
        }

        #endregion

        private bool IsSameLobby(Lobby lobby) => this.lobby.HasValue && this.lobby.Value.Id.Value == lobby.Id.Value;

        private bool IsBuildCompatible(Lobby lobby)
        {
            string remote = lobby.GetData(BuildIdKey);

            // Пустое значение означает, что хост ещё не успел записать метаданные.
            // Считаем это совместимым: реальную проверку версий всё равно должен делать
            // Authenticator на стороне сервера (см. документацию).
            return string.IsNullOrEmpty(remote) || remote == buildId;
        }

        private void TryReadHostAddress(Lobby lobby)
        {
            string address = lobby.GetData(HostAddressKey);
            if (string.IsNullOrEmpty(address))
                return;

            SetHostAddress(address);
        }

        private void SetHostAddress(string address)
        {
            address ??= string.Empty;
            if (hostAddress == address)
                return;

            hostAddress = address;

            if (!string.IsNullOrEmpty(address))
                HostAddressChanged?.Invoke(address);
        }
    }
}
