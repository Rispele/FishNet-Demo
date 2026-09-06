namespace Coop.Core.Sessions
{
    /// <summary>
    /// Абстракция «локальный игрок» — то, что мы знаем о пользователе до входа в сессию.
    ///
    /// Слой Networking зависит только от этого интерфейса и ничего не знает о Steam.
    /// Благодаря этому весь сетевой код можно запустить без Steam (LAN-режим, автотесты,
    /// dedicated server), просто подставив другую реализацию.
    /// </summary>
    public interface ILocalUser
    {
        /// <summary>True, если платформенный сервис инициализирован и данные достоверны.</summary>
        bool IsValid { get; }

        /// <summary>Стабильный идентификатор пользователя на платформе (SteamID64 для Steam).</summary>
        ulong Id { get; }

        /// <summary>Отображаемое имя. Никогда не null; при недоступности платформы — заглушка.</summary>
        string DisplayName { get; }
    }
}
