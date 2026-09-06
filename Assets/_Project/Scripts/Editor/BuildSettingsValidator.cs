using System.Collections.Generic;
using System.Linq;
using Coop.Core;
using UnityEditor;
using UnityEngine;

namespace Coop.Editor
{
    /// <summary>
    /// Проверка и автопочинка списка сцен в Build Settings.
    ///
    /// Порядок сцен — это неявная зависимость, которую очень легко сломать: сцена не добавлена
    /// в билд → в редакторе всё работает, а в собранной игре загрузка молча падает.
    /// Такие вещи стоит проверять инструментом, а не устно в чеклисте перед релизом.
    /// </summary>
    public static class BuildSettingsValidator
    {
        private const string SceneFolder = "Assets/_Project/Scenes";

        [MenuItem("Coop/Validate Build Settings", priority = 0)]
        public static void Validate()
        {
            List<string> problems = new();
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            for (int i = 0; i < SceneCatalog.All.Length; i++)
            {
                string expected = ScenePath(SceneCatalog.All[i]);

                if (!System.IO.File.Exists(expected))
                {
                    problems.Add($"Сцена не найдена на диске: {expected}");
                    continue;
                }

                int index = System.Array.FindIndex(scenes, s => s.path == expected);
                if (index < 0)
                    problems.Add($"Сцена не добавлена в Build Settings: {expected}");
                else if (index != i)
                    problems.Add($"Сцена {SceneCatalog.All[i]} стоит на позиции {index}, ожидалась {i}.");
                else if (!scenes[index].enabled)
                    problems.Add($"Сцена {SceneCatalog.All[i]} выключена в Build Settings.");
            }

            if (problems.Count == 0)
            {
                Debug.Log("[Coop] Build Settings в порядке.");
                return;
            }

            Debug.LogWarning($"[Coop] Проблемы Build Settings:\n- {string.Join("\n- ", problems)}\n" +
                             "Меню Coop → Fix Build Settings исправит порядок автоматически.");
        }

        [MenuItem("Coop/Fix Build Settings", priority = 1)]
        public static void Fix()
        {
            List<EditorBuildSettingsScene> ordered = SceneCatalog.All
                .Select(ScenePath)
                .Where(System.IO.File.Exists)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToList();

            // Сцены, не входящие в каталог, сохраняем в конце — они могут быть чьими-то ещё.
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (ordered.All(s => s.path != scene.path))
                    ordered.Add(scene);
            }

            EditorBuildSettings.scenes = ordered.ToArray();
            Debug.Log($"[Coop] Build Settings обновлены: {ordered.Count} сцен.");
        }

        private static string ScenePath(string sceneName) => $"{SceneFolder}/{sceneName}.unity";
    }
}
