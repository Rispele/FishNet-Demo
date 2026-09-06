namespace Coop.Core.Sessions
{
    /// <summary>Состояние сетевой сессии с точки зрения локального приложения.</summary>
    public enum SessionState
    {
        /// <summary>Сессии нет: главное меню.</summary>
        Offline,

        /// <summary>Идёт создание лобби и запуск сервера.</summary>
        Hosting,

        /// <summary>Идёт вход в лобби и подключение к хосту.</summary>
        Connecting,

        /// <summary>Мы в сессии: лобби или игра (за фазу отвечает SessionCoordinator).</summary>
        Connected,

        /// <summary>Идёт корректное завершение сессии.</summary>
        Disconnecting
    }
}
