using UnityEngine;
using UnityEngine.Serialization;

namespace Coop.Core.Config
{
    /// <summary>
    /// Конфигурация приложения в виде ScriptableObject.
    ///
    /// Почему ScriptableObject, а не константы в коде:
    /// * значения можно менять без перекомпиляции;
    /// * один и тот же ассет переиспользуется всеми системами, нет рассинхронизации;
    /// * для тестов/дев-режима легко подложить другой ассет.
    /// </summary>
    [CreateAssetMenu(menuName = "Coop/App Config", fileName = "AppConfig")]
    public sealed class AppConfig : ScriptableObject
    {
        [FormerlySerializedAs("_steamAppId")]
        [Header("Steam")]
        [Tooltip("AppId вашей игры в Steam. 480 — Spacewar, публичный тестовый AppId, " +
                 "который Valve разрешает использовать для разработки.")]
        [SerializeField] private uint steamAppId = 480;

        [FormerlySerializedAs("_buildId")]
        [Tooltip("Идентификатор сборки. Пишется в метаданные лобби, чтобы клиент старой версии " +
                 "не пытался подключиться к хосту новой.")]
        [SerializeField] private string buildId = "coop-demo-1";

        [FormerlySerializedAs("_maxPlayers")]
        [Header("Session")]
        [Tooltip("Максимальное число игроков в сессии, включая хоста.")]
        [Range(2, 16)]
        [SerializeField] private int maxPlayers = 4;

        [FormerlySerializedAs("_forceLanMode")]
        [Header("LAN fallback")]
        [Tooltip("Принудительно использовать LAN-режим (Tugboat, прямой IP) даже если Steam доступен. " +
                 "Нужно для локальной отладки: через Steam P2P нельзя подключиться к самому себе " +
                 "с одного и того же аккаунта.")]
        [SerializeField] private bool forceLanMode;

        [FormerlySerializedAs("_lanAddress")]
        [Tooltip("Адрес, к которому подключается LAN-клиент.")]
        [SerializeField] private string lanAddress = "127.0.0.1";

        public uint SteamAppId => steamAppId;
        public string BuildId => buildId;
        public int MaxPlayers => maxPlayers;
        public bool ForceLanMode => forceLanMode;
        public string LanAddress => lanAddress;
    }
}
