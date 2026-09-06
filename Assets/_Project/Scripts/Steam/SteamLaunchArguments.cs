using System;
using Steamworks;

namespace Coop.Steam
{
    /// <summary>
    /// Разбор аргументов запуска, которые Steam подставляет при принятии приглашения.
    ///
    /// Сценарий: друг присылает приглашение, а игра у нас не запущена. Steam стартует игру
    /// с аргументом "+connect_lobby (SteamID64 лобби)". Приложение обязано разобрать его само —
    /// колбэка в этом случае не будет, потому что игра ещё не работала в момент, когда
    /// пользователь нажал «Присоединиться».
    ///
    /// Второй сценарий (игра уже запущена) обрабатывается событием
    /// SteamFriends.OnGameLobbyJoinRequested — см. <see cref="SteamLobbyService"/>.
    /// </summary>
    public static class SteamLaunchArguments
    {
        private const string ConnectLobbyArgument = "+connect_lobby";

        /// <summary>
        /// Возвращает ключ лобби, если приложение запущено по приглашению.
        /// </summary>
        public static bool TryGetPendingLobby(out string lobbyKey)
        {
            // Steam отдаёт «свою» командную строку даже там, где ОС её не пробрасывает
            // (например, при запуске через протокол steam://).
            if (SteamClient.IsValid && TryParse(SteamApps.CommandLine, out lobbyKey))
                return true;

            return TryParse(string.Join(" ", Environment.GetCommandLineArgs()), out lobbyKey);
        }

        private static bool TryParse(string commandLine, out string lobbyKey)
        {
            lobbyKey = null;

            if (string.IsNullOrEmpty(commandLine))
                return false;

            string[] parts = commandLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!string.Equals(parts[i], ConnectLobbyArgument, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!ulong.TryParse(parts[i + 1], out ulong id) || id == 0UL)
                    continue;

                lobbyKey = id.ToString();
                return true;
            }

            return false;
        }
    }
}
