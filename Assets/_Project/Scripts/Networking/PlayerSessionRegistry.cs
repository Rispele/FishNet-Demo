using System;
using System.Collections.Generic;
using UnityEngine;

namespace Coop.Networking
{
    /// <summary>
    /// Реестр всех <see cref="PlayerSession"/>, известных этому процессу.
    ///
    /// Зачем он нужен: сетевые объекты появляются и исчезают асинхронно, а UI лобби должен
    /// показывать актуальный список игроков. Опрашивать сцену через FindObjectsOfType каждый
    /// кадр — плохо и по производительности, и по надёжности (объект может быть в
    /// DontDestroyOnLoad). Реестр даёт O(1)-регистрацию и одно событие на изменение.
    ///
    /// Реестр статический сознательно: он привязан к жизненному циклу процесса, а не сцены,
    /// и заполняется исключительно самими сетевыми объектами в OnStartNetwork/OnStopNetwork.
    /// Записи всегда парные, поэтому «протухших» ссылок не остаётся.
    /// </summary>
    public static class PlayerSessionRegistry
    {
        private static readonly List<PlayerSession> Sessions = new();

        /// <summary>Все известные профили игроков. Порядок соответствует порядку подключения.</summary>
        public static IReadOnlyList<PlayerSession> All => Sessions;

        /// <summary>
        /// Профиль локального игрока. Null, пока сервер его не заспавнил.
        ///
        /// Свойство вычисляется, а не кэшируется при регистрации: OnStartNetwork может
        /// сработать раньше, чем FishNet назначит объекту владельца, и закэшированное
        /// значение оказалось бы пустым навсегда.
        /// </summary>
        public static PlayerSession Local
        {
            get
            {
                for (int i = 0; i < Sessions.Count; i++)
                {
                    if (Sessions[i] != null && Sessions[i].IsLocal)
                        return Sessions[i];
                }

                return null;
            }
        }

        /// <summary>Список игроков или их данные изменились.</summary>
        public static event Action Changed;

        internal static void Register(PlayerSession session)
        {
            if (session == null || Sessions.Contains(session))
                return;

            Sessions.Add(session);
            NotifyChanged();
        }

        internal static void Unregister(PlayerSession session)
        {
            if (session == null || !Sessions.Remove(session))
                return;

            NotifyChanged();
        }

        internal static void NotifyChanged() => Changed?.Invoke();

        /// <summary>True, если игроков хотя бы один и все отметились готовыми.</summary>
        public static bool AllReady()
        {
            if (Sessions.Count == 0)
                return false;

            for (int i = 0; i < Sessions.Count; i++)
            {
                if (!Sessions[i].IsReady)
                    return false;
            }

            return true;
        }

        /// <summary>Находит профиль по идентификатору соединения FishNet.</summary>
        public static PlayerSession FindByClientId(int clientId)
        {
            for (int i = 0; i < Sessions.Count; i++)
            {
                PlayerSession session = Sessions[i];
                if (session.Owner != null && session.Owner.ClientId == clientId)
                    return session;
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Sessions.Clear();
            Changed = null;
        }
    }
}
