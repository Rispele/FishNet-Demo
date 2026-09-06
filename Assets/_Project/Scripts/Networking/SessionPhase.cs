namespace Coop.Networking
{
    /// <summary>
    /// Фаза сессии. В отличие от <see cref="Coop.Core.Sessions.SessionState"/>, которая описывает
    /// локальное состояние подключения, фаза — это состояние всей сессии, реплицируемое сервером.
    /// </summary>
    public enum SessionPhase : byte
    {
        /// <summary>Игроки собираются и отмечают готовность.</summary>
        Lobby = 0,

        /// <summary>Матч идёт: загружена игровая сцена, персонажи заспавнены.</summary>
        Playing = 1
    }
}
