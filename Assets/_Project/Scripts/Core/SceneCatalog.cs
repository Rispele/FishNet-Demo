namespace Coop.Core
{
    /// <summary>
    /// Единственный источник правды об именах сцен.
    /// Строковые литералы сцен разбросанные по коду — классический источник багов,
    /// поэтому имена собраны здесь и проверяются редакторным валидатором
    /// (см. Coop.Editor.BuildSettingsValidator).
    /// </summary>
    public static class SceneCatalog
    {
        /// <summary>Точка входа приложения. Содержит только composition root.</summary>
        public const string Bootstrap = "Bootstrap";

        /// <summary>Оффлайн-сцена главного меню. Загружается обычным UnityEngine.SceneManagement.</summary>
        public const string MainMenu = "MainMenu";

        /// <summary>Сетевая сцена лобби. Загружается FishNet SceneManager как глобальная сцена.</summary>
        public const string Lobby = "Lobby";

        /// <summary>Сетевая игровая сцена. Загружается FishNet SceneManager как глобальная сцена.</summary>
        public const string Game = "Game";

        /// <summary>Все сцены проекта в порядке, в котором они должны лежать в Build Settings.</summary>
        public static readonly string[] All = { Bootstrap, MainMenu, Lobby, Game };
    }
}
