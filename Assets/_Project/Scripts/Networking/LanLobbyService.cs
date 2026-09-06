using System;
using System.Threading.Tasks;
using Coop.Core.Diagnostics;
using Coop.Core.Sessions;

namespace Coop.Networking
{
    /// <summary>
    /// Заглушка <see cref="ILobbyService"/> для локальной отладки без Steam.
    ///
    /// Зачем она нужна на практике:
    /// * через Steam P2P нельзя подключиться к самому себе с одного аккаунта, поэтому
    ///   протестировать two-player flow в редакторе + билде на одной машине через Steam нельзя;
    /// * на CI и в автотестах Steam недоступен в принципе;
    /// * это лучшая демонстрация того, что сетевой код не зависит от платформы:
    ///   тот же <see cref="SessionManager"/> без единого изменения работает и по Steam,
    ///   и по прямому IP через Tugboat.
    ///
    /// «Лобби» здесь чисто виртуальное: комнаты не существует, есть только заранее известный адрес.
    /// </summary>
    public sealed class LanLobbyService : ILobbyService
    {
        private const string LogContext = "LanLobby";

        private readonly string fallbackAddress;
        private bool inLobby;
        private bool isOwner;

        public LanLobbyService(string fallbackAddress)
        {
            this.fallbackAddress = string.IsNullOrEmpty(fallbackAddress) ? "127.0.0.1" : fallbackAddress;
            HostAddress = this.fallbackAddress;
        }

        public bool IsAvailable => true;
        public bool IsInLobby => inLobby;
        public bool IsOwner => isOwner;
        public bool SupportsInvites => false;
        public string HostAddress { get; private set; }
        public string LobbyKey => fallbackAddress;

        public event Action Entered;
        public event Action Exited;
        public event Action<string> HostAddressChanged;

        // В LAN-режиме нет ни комнаты со списком участников, ни приглашений: эти события
        // объявлены только ради контракта интерфейса и никогда не вызываются.
        #pragma warning disable 67
        public event Action MembersChanged;
        public event Action<string> JoinRequested;
        public event Action<string> Failed;
        #pragma warning restore 67

        public Task<bool> CreateAsync(int maxMembers)
        {
            inLobby = true;
            isOwner = true;
            CoopLog.Info(LogContext, "LAN-хост: лобби эмулировано.");
            Entered?.Invoke();
            return Task.FromResult(true);
        }

        public Task<bool> JoinAsync(string lobbyKey)
        {
            inLobby = true;
            isOwner = false;
            HostAddress = string.IsNullOrEmpty(lobbyKey) ? fallbackAddress : lobbyKey;

            CoopLog.Info(LogContext, $"LAN-клиент: подключаемся к {HostAddress}.");
            Entered?.Invoke();
            HostAddressChanged?.Invoke(HostAddress);
            return Task.FromResult(true);
        }

        public void Leave()
        {
            if (!inLobby)
                return;

            inLobby = false;
            isOwner = false;
            Exited?.Invoke();
        }

        public void PublishHostAddress(string address)
        {
            HostAddress = string.IsNullOrEmpty(address) ? fallbackAddress : address;
        }

        public void OpenInviteOverlay()
            => CoopLog.Warning(LogContext, "Приглашения недоступны в LAN-режиме.");

        public void SetJoinable(bool joinable)
        {
            // Прямое IP-подключение не имеет понятия «открытость лобби»:
            // за это отвечает ServerManager (StartConnection/StopConnection).
        }

        public void Dispose() => Leave();
    }
}
