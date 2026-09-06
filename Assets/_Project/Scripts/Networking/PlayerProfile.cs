using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// Снимок данных локального игрока, доступный сетевому слою.
    ///
    /// Зачем нужен: сетевые объекты создаются FishNet'ом, а не нами, поэтому им нельзя
    /// «внедрить» зависимости конструктором. Вместо того чтобы тащить в сетевой слой
    /// ссылку на Steam, composition root один раз кладёт сюда уже готовые значения.
    ///
    /// Это осознанный компромисс: крошечное, доступное только на запись из одного места
    /// хранилище вместо зависимости Coop.Networking → Coop.Steam.
    /// </summary>
    public static class PlayerProfile
    {
        /// <summary>Имя локального игрока (Steam-ник или заглушка в LAN-режиме).</summary>
        public static string LocalDisplayName { get; private set; } = "Player";

        /// <summary>SteamID64 локального игрока; 0, если платформа недоступна.</summary>
        public static ulong LocalPlatformId { get; private set; }

        /// <summary>Вызывается composition root'ом до старта любой сессии.</summary>
        public static void Set(string displayName, ulong platformId)
        {
            LocalDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
            LocalPlatformId = platformId;
        }

        /// <summary>
        /// Сбрасывает состояние при старте игры. Нужно для режима «Enter Play Mode без
        /// перезагрузки домена», где статические поля сохраняются между запусками.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LocalDisplayName = "Player";
            LocalPlatformId = 0UL;
        }
    }
}
