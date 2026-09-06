using System;
using System.Threading.Tasks;

namespace Coop.Core.Sessions
{
    /// <summary>
    /// Абстракция «matchmaking-бэкенда».
    ///
    /// Ключевая идея архитектуры: лобби и транспорт — это РАЗНЫЕ вещи.
    /// * Лобби (Steam Matchmaking) отвечает за то, как игроки находят друг друга,
    ///   как работают приглашения и где хранятся метаданные комнаты.
    /// * Транспорт (FishNet + FishyFacepunch/Tugboat) отвечает за то, как байты
    ///   доходят от клиента к серверу.
    ///
    /// Связь между ними — ровно одна строка: <see cref="HostAddress"/>.
    /// Хост публикует свой адрес в метаданные лобби, клиент его читает и передаёт транспорту.
    /// Поэтому замена Steam на EOS/Playfab/LAN не затрагивает сетевой код вообще.
    /// </summary>
    public interface ILobbyService : IDisposable
    {
        /// <summary>Доступен ли бэкенд (например, запущен ли Steam).</summary>
        bool IsAvailable { get; }

        /// <summary>Находимся ли мы сейчас в лобби.</summary>
        bool IsInLobby { get; }

        /// <summary>Являемся ли мы владельцем (хостом) текущего лобби.</summary>
        bool IsOwner { get; }

        /// <summary>Поддерживает ли бэкенд приглашения через оверлей платформы.</summary>
        bool SupportsInvites { get; }

        /// <summary>
        /// Адрес хоста для транспорта. Формат зависит от транспорта:
        /// SteamID64 для FishyFacepunch, IP-адрес для Tugboat.
        /// Пустая строка означает «хост ещё не опубликовал адрес».
        /// </summary>
        string HostAddress { get; }

        /// <summary>Ключ, который нужно передать в <see cref="JoinAsync"/>, чтобы войти в это лобби.</summary>
        string LobbyKey { get; }

        /// <summary>Мы вошли в лобби (событие приходит и хосту, и клиенту).</summary>
        event Action Entered;

        /// <summary>Мы покинули лобби.</summary>
        event Action Exited;

        /// <summary>Состав лобби изменился.</summary>
        event Action MembersChanged;

        /// <summary>
        /// Хост опубликовал (или изменил) адрес. Клиент подключается именно по этому событию,
        /// потому что в момент входа в лобби адреса может ещё не быть.
        /// </summary>
        event Action<string> HostAddressChanged;

        /// <summary>
        /// Пользователь принял приглашение или нажал Join в оверлее платформы.
        /// Аргумент — ключ лобби для <see cref="JoinAsync"/>.
        /// </summary>
        event Action<string> JoinRequested;

        /// <summary>Ошибка бэкенда, пригодная для показа пользователю.</summary>
        event Action<string> Failed;

        /// <summary>Создать лобби и стать его владельцем.</summary>
        Task<bool> CreateAsync(int maxMembers);

        /// <summary>Войти в существующее лобби по ключу.</summary>
        Task<bool> JoinAsync(string lobbyKey);

        /// <summary>Покинуть лобби. Безопасно вызывать, если мы не в лобби.</summary>
        void Leave();

        /// <summary>Опубликовать адрес хоста в метаданных лобби. Вызывает только владелец.</summary>
        void PublishHostAddress(string address);

        /// <summary>Открыть платформенный оверлей приглашения друзей.</summary>
        void OpenInviteOverlay();

        /// <summary>Разрешить/запретить вход новых игроков (например, после старта матча).</summary>
        void SetJoinable(bool joinable);

    }
}
