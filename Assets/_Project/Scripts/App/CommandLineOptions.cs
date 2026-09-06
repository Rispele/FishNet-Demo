using System;

namespace Coop.App
{
    /// <summary>
    /// Аргументы командной строки для автоматизации запуска.
    ///
    /// Зачем это нужно в кооп-проекте: чтобы протестировать хотя бы двух игроков, нужно
    /// поднять два процесса. Кликать в двух окнах руками — медленно и невоспроизводимо,
    /// поэтому в любом сетевом проекте рано или поздно появляются такие флаги. Они же
    /// используются CI и выделенным сервером.
    ///
    /// Поддерживаются:
    ///   -autohost               — сразу создать сессию;
    ///   -autojoin &lt;адрес&gt;      — сразу подключиться (IP для Tugboat, SteamID64 лобби для Steam);
    ///   -lan                    — принудительно LAN-режим, даже если Steam доступен.
    /// </summary>
    public readonly struct CommandLineOptions
    {
        public readonly bool autoHost;
        public readonly string autoJoinAddress;
        public readonly bool forceLan;

        private CommandLineOptions(bool autoHost, string autoJoinAddress, bool forceLan)
        {
            this.autoHost = autoHost;
            this.autoJoinAddress = autoJoinAddress;
            this.forceLan = forceLan;
        }

        public bool HasAutoJoin => !string.IsNullOrEmpty(autoJoinAddress);

        public static CommandLineOptions Parse()
        {
            string[] args = Environment.GetCommandLineArgs();

            bool autoHost = false;
            bool forceLan = false;
            string joinAddress = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-autohost":
                        autoHost = true;
                        break;

                    case "-lan":
                        forceLan = true;
                        break;

                    case "-autojoin":
                        if (i + 1 < args.Length)
                            joinAddress = args[i + 1];
                        break;
                }
            }

            return new CommandLineOptions(autoHost, joinAddress, forceLan);
        }
    }
}