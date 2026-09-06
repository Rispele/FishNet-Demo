// Одноразовый билдер ассетов демо-проекта.
// Живёт вне Assets/, запускается через Unity MCP (run_script) и не попадает в игру.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Coop.App;
using Coop.Core;
using Coop.Core.Config;
using Coop.Gameplay;
using Coop.Networking;
using Coop.UI;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CoopBuild
{
    public static class CoopDemoBuilder
    {
        private const string Root = "Assets/_Project";
        private const string PrefabsNet = Root + "/Prefabs/Network";
        private const string PrefabsUi = Root + "/Prefabs/UI";
        private const string Scenes = Root + "/Scenes";
        private const string Materials = Root + "/Art/Materials";
        private const string Settings = Root + "/Settings";

        private static readonly Color Bg = new(0.09f, 0.10f, 0.13f, 0.94f);
        private static readonly Color Accent = new(0.20f, 0.55f, 0.95f, 1f);
        private static readonly Color Neutral = new(0.22f, 0.24f, 0.30f, 1f);

        public static string Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolders();
            BuildAssets();
            BuildPrefabs();
            BuildScenes();
            UpdateBuildSettings();
            AssetDatabase.SaveAssets();
            return "done";
        }

        public static string BuildAssets()
        {
            EnsureFolders();
            CreateMaterial("M_Ground", new Color(0.30f, 0.32f, 0.36f));
            CreateMaterial("M_Platform", new Color(0.38f, 0.42f, 0.48f));
            CreateMaterial("M_Obstacle", new Color(0.55f, 0.35f, 0.25f));
            CreateMaterial("M_Player", Color.white);
            CreateAppConfig();
            AssetDatabase.SaveAssets();
            return "assets ok";
        }

        public static string BuildNetworkPrefabs()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Debug.Log("[Builder] player session");
            GameObject playerSession = BuildPlayerSessionPrefab();
            Debug.Log("[Builder] player character");
            GameObject playerCharacter = BuildPlayerCharacterPrefab(Load<Material>($"{Materials}/M_Player.mat"));
            Debug.Log("[Builder] coordinator");
            GameObject coordinator = BuildSessionCoordinatorPrefab(playerSession, playerCharacter);
            Debug.Log("[Builder] session manager");
            BuildSessionManagerPrefab(coordinator);
            AssetDatabase.SaveAssets();
            return "network prefabs ok";
        }

        public static string BuildManagerPrefab(string which)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AppConfig config = Load<AppConfig>($"{Settings}/AppConfig.asset");
            bool steam = which == "steam";
            BuildNetworkManagerPrefab(steam ? "NetworkManager_Steam" : "NetworkManager_Lan", steam, config);
            AssetDatabase.SaveAssets();
            return which + " manager ok";
        }

        public static string BuildUiPrefabs()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLobbyEntryPrefab();
            AssetDatabase.SaveAssets();
            return "ui prefabs ok";
        }

        public static string BuildPrefabs()
        {
            BuildNetworkPrefabs();
            BuildManagerPrefab("steam");
            BuildManagerPrefab("lan");
            BuildUiPrefabs();
            return "prefabs ok";
        }

        public static string BuildSceneBootstrap()
        {
            BuildBootstrapScene();
            return "bootstrap ok";
        }

        public static string BuildSceneMainMenu()
        {
            BuildMainMenuScene();
            return "menu ok";
        }

        public static string BuildSceneLobby()
        {
            BuildLobbyScene(Load<GameObject>($"{PrefabsUi}/LobbyMemberEntry.prefab"));
            return "lobby ok";
        }

        public static string BuildSceneGame()
        {
            BuildGameScene(
                Load<Material>($"{Materials}/M_Ground.mat"),
                Load<Material>($"{Materials}/M_Platform.mat"),
                Load<Material>($"{Materials}/M_Obstacle.mat"));
            return "game ok";
        }

        public static string BuildScenes()
        {
            BuildSceneBootstrap();
            BuildSceneMainMenu();
            BuildSceneLobby();
            BuildSceneGame();
            return "scenes ok";
        }

        public static string FixBuildSettings()
        {
            UpdateBuildSettings();
            return "build settings ok";
        }

        private static T Load<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new Exception($"Ассет не найден: {path}");
            return asset;
        }

        #region Folders / assets

        private static void EnsureFolders()
        {
            foreach (string path in new[]
                     {
                         Root, Root + "/Prefabs", PrefabsNet, PrefabsUi, Scenes,
                         Root + "/Art", Materials, Settings
                     })
            {
                if (AssetDatabase.IsValidFolder(path))
                    continue;

                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = $"{Materials}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", 0.15f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static AppConfig CreateAppConfig()
        {
            string path = $"{Settings}/AppConfig.asset";
            AppConfig config = AssetDatabase.LoadAssetAtPath<AppConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<AppConfig>();
                AssetDatabase.CreateAsset(config, path);
            }

            SerializedObject so = new(config);
            so.FindProperty("_steamAppId").uintValue = 480;
            so.FindProperty("_buildId").stringValue = "coop-demo-1";
            so.FindProperty("_maxPlayers").intValue = 4;
            so.FindProperty("_forceLanMode").boolValue = false;
            so.FindProperty("_lanAddress").stringValue = "127.0.0.1";
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
        }

        #endregion

        #region Network prefabs

        private static GameObject BuildPlayerSessionPrefab()
        {
            GameObject go = new("PlayerSession");
            NetworkObject nob = go.AddComponent<NetworkObject>();
            SetGlobal(nob, true);
            go.AddComponent<PlayerSession>();

            return SaveAndDestroy(go, $"{PrefabsNet}/PlayerSession.prefab");
        }

        private static GameObject BuildSessionCoordinatorPrefab(GameObject playerSession, GameObject playerCharacter)
        {
            GameObject go = new("SessionCoordinator");
            NetworkObject nob = go.AddComponent<NetworkObject>();
            SetGlobal(nob, true);

            SessionCoordinator coordinator = go.AddComponent<SessionCoordinator>();
            SerializedObject so = new(coordinator);
            so.FindProperty("_playerSessionPrefab").objectReferenceValue = playerSession.GetComponent<NetworkObject>();
            so.FindProperty("_playerCharacterPrefab").objectReferenceValue = playerCharacter.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabsNet}/SessionCoordinator.prefab");
        }

        private static GameObject BuildSessionManagerPrefab(GameObject coordinator)
        {
            GameObject go = new("SessionManager");
            SessionManager manager = go.AddComponent<SessionManager>();

            SerializedObject so = new(manager);
            so.FindProperty("_sessionCoordinatorPrefab").objectReferenceValue = coordinator.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabsNet}/SessionManager.prefab");
        }

        private static GameObject BuildNetworkManagerPrefab(string name, bool steam, AppConfig config)
        {
            GameObject go = new(name);

            NetworkManager manager = go.AddComponent<NetworkManager>();
            TransportManager transportManager = go.AddComponent<TransportManager>();

            Component transport;
            if (steam)
            {
                Type type = FindType("FishyFacepunch.FishyFacepunch");
                if (type == null)
                    throw new Exception("Тип FishyFacepunch не найден. Проверьте Assets/Plugins/FishyFacepunch.");

                transport = go.AddComponent(type);

                SerializedObject tso = new(transport);
                tso.FindProperty("_steamAppID").uintValue = config.SteamAppId;
                tso.FindProperty("_maximumClients").intValue = config.MaxPlayers;
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                transport = go.AddComponent<FishNet.Transporting.Tugboat.Tugboat>();

                SerializedObject tso = new(transport);
                SerializedProperty max = tso.FindProperty("_maximumClients");
                if (max != null)
                    max.intValue = config.MaxPlayers;
                tso.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedObject tmso = new(transportManager);
            tmso.FindProperty("Transport").objectReferenceValue = transport;
            tmso.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject nso = new(manager);
            nso.FindProperty("_dontDestroyOnLoad").boolValue = true;
            nso.FindProperty("_runInBackground").boolValue = true;
            SerializedProperty spawnables = nso.FindProperty("_spawnablePrefabs");
            if (spawnables != null)
                spawnables.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>("Assets/DefaultPrefabObjects.asset");
            nso.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabsNet}/{name}.prefab");
        }

        private static GameObject BuildPlayerCharacterPrefab(Material playerMat)
        {
            GameObject go = new("PlayerCharacter");

            CharacterController controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.35f;
            controller.skinWidth = 0.03f;

            NetworkObject nob = go.AddComponent<NetworkObject>();

            // Обязательно для предсказанного персонажа: без Enable Prediction FishNet не шлёт
            // наблюдателям replicate/reconcile-состояния, и чужие игроки видят его неподвижным
            // (EnableStateForwarding = _enablePrediction && _enableStateForwarding).
            SetPredictionEnabled(nob, true);

            // --- Графика (отдельный объект, чтобы её можно было сглаживать между тиками) ---
            GameObject graphics = CreatePrimitive(PrimitiveType.Capsule, "Graphics", go.transform,
                new Vector3(0f, 1f, 0f), new Vector3(0.8f, 1f, 0.8f), playerMat, keepCollider: false);

            CreatePrimitive(PrimitiveType.Cube, "Facing", graphics.transform,
                new Vector3(0f, 0.28f, 0.78f), new Vector3(0.30f, 0.30f, 0.55f), playerMat, keepCollider: false);

            // NetworkTickSmoother сглаживает графику между сетевыми тиками.
            Type smootherType = FindType("FishNet.Component.Transforming.Beta.NetworkTickSmoother");
            if (smootherType != null)
            {
                Component smoother = graphics.AddComponent(smootherType);
                SerializedObject sso = new(smoother);
                SerializedProperty target = sso.FindProperty("_initializationSettings.TargetTransform");
                if (target != null)
                    target.objectReferenceValue = go.transform;
                sso.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- Ник над головой ---
            GameObject nameplateGo = new("Nameplate");
            nameplateGo.transform.SetParent(go.transform, false);
            nameplateGo.transform.localPosition = new Vector3(0f, 2.45f, 0f);

            TextMeshPro label = nameplateGo.AddComponent<TextMeshPro>();
            label.text = "Player";
            label.fontSize = 3f;
            label.alignment = TextAlignmentOptions.Center;
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;
            RectTransform labelRect = nameplateGo.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(4f, 1f);

            PlayerNameplate nameplate = nameplateGo.AddComponent<PlayerNameplate>();
            SerializedObject npso = new(nameplate);
            npso.FindProperty("_label").objectReferenceValue = label;
            npso.ApplyModifiedPropertiesWithoutUndo();

            // --- Логика ---
            PlayerInputReader input = go.AddComponent<PlayerInputReader>();
            SerializedObject iso = new(input);
            iso.FindProperty("_actionsAsset").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            iso.ApplyModifiedPropertiesWithoutUndo();

            go.AddComponent<PlayerMotor>();

            PlayerAppearance appearance = go.AddComponent<PlayerAppearance>();
            SerializedObject aso = new(appearance);
            SerializedProperty renderers = aso.FindProperty("_renderers");
            Renderer[] found = go.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer).ToArray();
            renderers.arraySize = found.Length;
            for (int i = 0; i < found.Length; i++)
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            aso.ApplyModifiedPropertiesWithoutUndo();

            PlayerCharacter character = go.AddComponent<PlayerCharacter>();
            SerializedObject cso = new(character);
            cso.FindProperty("_appearance").objectReferenceValue = appearance;
            cso.FindProperty("_nameplate").objectReferenceValue = nameplate;
            cso.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabsNet}/PlayerCharacter.prefab");
        }

        /// <summary>
        /// Включает предсказание на NetworkObject.
        ///
        /// Графический объект намеренно не назначается: сглаживанием занимается
        /// NetworkTickSmoother на дочернем объекте — так же, как в официальном демо
        /// FishNet по CharacterController-предсказанию.
        /// </summary>
        private static void SetPredictionEnabled(NetworkObject nob, bool value)
        {
            SerializedObject so = new(nob);
            so.FindProperty("_enablePrediction").boolValue = value;
            so.FindProperty("_enableStateForwarding").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetGlobal(NetworkObject nob, bool value)
        {
            SerializedObject so = new(nob);
            SerializedProperty prop = so.FindProperty("_isGlobal");
            if (prop != null)
                prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion

        #region UI prefabs

        private static GameObject BuildLobbyEntryPrefab()
        {
            GameObject go = new("LobbyMemberEntry", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 56f);

            Image bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.05f);

            LayoutElement layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 56f;
            layout.preferredHeight = 56f;

            GameObject swatchGo = new("Swatch", typeof(RectTransform), typeof(Image));
            swatchGo.transform.SetParent(go.transform, false);
            RectTransform swatchRect = swatchGo.GetComponent<RectTransform>();
            swatchRect.anchorMin = new Vector2(0f, 0.5f);
            swatchRect.anchorMax = new Vector2(0f, 0.5f);
            swatchRect.pivot = new Vector2(0f, 0.5f);
            swatchRect.anchoredPosition = new Vector2(16f, 0f);
            swatchRect.sizeDelta = new Vector2(24f, 24f);
            Image swatch = swatchGo.GetComponent<Image>();
            swatch.color = Accent;

            TextMeshProUGUI nameLabel = CreateUguiLabel(go.transform, "Name", "Player", 26,
                TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(52f, 0f), new Vector2(-160f, 0f));

            TextMeshProUGUI statusLabel = CreateUguiLabel(go.transform, "Status", "не готов", 22,
                TextAlignmentOptions.MidlineRight, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-160f, 0f), new Vector2(-16f, 0f));

            LobbyMemberEntry entry = go.AddComponent<LobbyMemberEntry>();
            SerializedObject so = new(entry);
            so.FindProperty("_colorSwatch").objectReferenceValue = swatch;
            so.FindProperty("_nameLabel").objectReferenceValue = nameLabel;
            so.FindProperty("_statusLabel").objectReferenceValue = statusLabel;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAndDestroy(go, $"{PrefabsUi}/LobbyMemberEntry.prefab");
        }

        #endregion

        #region Scenes

        private static void BuildBootstrapScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Ассеты грузим ПОСЛЕ создания сцены: NewScene выгружает неиспользуемые ассеты,
            // и ссылка, взятая до него, успевает стать «уничтоженной» (Unity-null).
            AppConfig config = Load<AppConfig>($"{Settings}/AppConfig.asset");
            GameObject nmSteam = Load<GameObject>($"{PrefabsNet}/NetworkManager_Steam.prefab");
            GameObject nmLan = Load<GameObject>($"{PrefabsNet}/NetworkManager_Lan.prefab");
            GameObject sessionManager = Load<GameObject>($"{PrefabsNet}/SessionManager.prefab");

            GameObject camera = new("Bootstrap Camera", typeof(Camera));
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = new Color(0.05f, 0.06f, 0.08f);

            GameObject go = new("[Bootstrap]");
            GameBootstrap bootstrap = go.AddComponent<GameBootstrap>();

            SerializedObject so = new(bootstrap);
            so.FindProperty("_config").objectReferenceValue = config;
            so.FindProperty("_steamNetworkManagerPrefab").objectReferenceValue = nmSteam.GetComponent<NetworkManager>();
            so.FindProperty("_lanNetworkManagerPrefab").objectReferenceValue = nmLan.GetComponent<NetworkManager>();
            so.FindProperty("_sessionManagerPrefab").objectReferenceValue = sessionManager.GetComponent<SessionManager>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, $"{Scenes}/{SceneCatalog.Bootstrap}.unity");
        }

        private static void BuildMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateUiCamera("Menu Camera", new Color(0.06f, 0.07f, 0.10f));
            CreateEventSystem();

            GameObject canvas = CreateCanvas("MenuCanvas");
            GameObject panel = CreatePanel(canvas.transform, "Panel", new Vector2(760f, 620f));

            TextMeshProUGUI title = CreateUguiLabel(panel.transform, "Title", "Кооп-демо", 56,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -110f), new Vector2(-24f, -32f));

            TextMeshProUGUI player = CreateUguiLabel(panel.transform, "Player", "Вы: —", 26,
                TextAlignmentOptions.Center, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -160f), new Vector2(-24f, -114f));

            Button host = CreateButton(panel.transform, "HostButton", "Создать игру", Accent,
                new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(420f, 72f));

            GameObject lanPanel = new("LanPanel", typeof(RectTransform));
            lanPanel.transform.SetParent(panel.transform, false);
            RectTransform lanRect = lanPanel.GetComponent<RectTransform>();
            lanRect.anchorMin = new Vector2(0.5f, 1f);
            lanRect.anchorMax = new Vector2(0.5f, 1f);
            lanRect.pivot = new Vector2(0.5f, 1f);
            lanRect.anchoredPosition = new Vector2(0f, -300f);
            lanRect.sizeDelta = new Vector2(420f, 150f);

            TMP_InputField address = CreateInputField(lanPanel.transform, "AddressField", "127.0.0.1",
                new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(420f, 60f));

            Button join = CreateButton(lanPanel.transform, "JoinButton", "Подключиться", Neutral,
                new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(420f, 60f));

            Button quit = CreateButton(panel.transform, "QuitButton", "Выход", Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(420f, 60f));

            TextMeshProUGUI status = CreateUguiLabel(panel.transform, "Status", "", 22,
                TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 24f), new Vector2(-24f, 100f));

            MainMenuView view = canvas.AddComponent<MainMenuView>();
            SerializedObject so = new(view);
            so.FindProperty("_hostButton").objectReferenceValue = host;
            so.FindProperty("_joinButton").objectReferenceValue = join;
            so.FindProperty("_quitButton").objectReferenceValue = quit;
            so.FindProperty("_titleLabel").objectReferenceValue = title;
            so.FindProperty("_playerLabel").objectReferenceValue = player;
            so.FindProperty("_statusLabel").objectReferenceValue = status;
            so.FindProperty("_lanPanel").objectReferenceValue = lanPanel;
            so.FindProperty("_addressField").objectReferenceValue = address;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, $"{Scenes}/{SceneCatalog.MainMenu}.unity");
        }

        private static void BuildLobbyScene(GameObject entryPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateUiCamera("Lobby Camera", new Color(0.07f, 0.08f, 0.11f));
            CreateEventSystem();

            GameObject canvas = CreateCanvas("LobbyCanvas");
            GameObject panel = CreatePanel(canvas.transform, "Panel", new Vector2(880f, 720f));

            CreateUguiLabel(panel.transform, "Title", "Лобби", 48, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -96f), new Vector2(-24f, -28f));

            GameObject listGo = new("PlayerList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            listGo.transform.SetParent(panel.transform, false);
            RectTransform listRect = listGo.GetComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.offsetMin = new Vector2(32f, 0f);
            listRect.offsetMax = new Vector2(-32f, -120f);
            listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, 360f);

            VerticalLayoutGroup layout = listGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = listGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI hint = CreateUguiLabel(panel.transform, "Hint", "", 22,
                TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 236f), new Vector2(-24f, 300f));

            Button ready = CreateButton(panel.transform, "ReadyButton", "Готов", Accent,
                new Vector2(0.5f, 0f), new Vector2(0f, 196f), new Vector2(420f, 64f));
            Button start = CreateButton(panel.transform, "StartButton", "Начать матч", new Color(0.25f, 0.65f, 0.35f),
                new Vector2(0.5f, 0f), new Vector2(0f, 126f), new Vector2(420f, 64f));
            Button invite = CreateButton(panel.transform, "InviteButton", "Пригласить друзей", Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(420f, 56f));
            Button leave = CreateButton(panel.transform, "LeaveButton", "Покинуть лобби", new Color(0.45f, 0.20f, 0.22f),
                new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(420f, 56f));

            LobbyView view = canvas.AddComponent<LobbyView>();
            SerializedObject so = new(view);
            so.FindProperty("_listRoot").objectReferenceValue = listRect;
            so.FindProperty("_entryPrefab").objectReferenceValue = entryPrefab.GetComponent<LobbyMemberEntry>();
            so.FindProperty("_readyButton").objectReferenceValue = ready;
            so.FindProperty("_startButton").objectReferenceValue = start;
            so.FindProperty("_inviteButton").objectReferenceValue = invite;
            so.FindProperty("_leaveButton").objectReferenceValue = leave;
            so.FindProperty("_readyButtonLabel").objectReferenceValue = ready.GetComponentInChildren<TextMeshProUGUI>();
            so.FindProperty("_hintLabel").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, $"{Scenes}/{SceneCatalog.Lobby}.unity");
        }

        private static void BuildGameScene(Material groundMat, Material platformMat, Material obstacleMat)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.position = new Vector3(0f, 6f, -10f);
            camera.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
            cameraGo.AddComponent<PlayerCameraRig>();

            GameObject lightGo = new("Directional Light", typeof(Light));
            Light light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            GameObject level = new("Level");

            CreatePrimitive(PrimitiveType.Cube, "Ground", level.transform,
                new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), groundMat);

            // Стены по периметру, чтобы игроки не убегали за карту.
            CreatePrimitive(PrimitiveType.Cube, "Wall_N", level.transform, new Vector3(0f, 1.5f, 30f), new Vector3(60f, 3f, 1f), obstacleMat);
            CreatePrimitive(PrimitiveType.Cube, "Wall_S", level.transform, new Vector3(0f, 1.5f, -30f), new Vector3(60f, 3f, 1f), obstacleMat);
            CreatePrimitive(PrimitiveType.Cube, "Wall_E", level.transform, new Vector3(30f, 1.5f, 0f), new Vector3(1f, 3f, 60f), obstacleMat);
            CreatePrimitive(PrimitiveType.Cube, "Wall_W", level.transform, new Vector3(-30f, 1.5f, 0f), new Vector3(1f, 3f, 60f), obstacleMat);

            // Немного препятствий и ступенек — чтобы было что тестировать: прыжки, склоны, коллизии.
            CreatePrimitive(PrimitiveType.Cube, "Platform_A", level.transform, new Vector3(8f, 0.5f, 6f), new Vector3(6f, 1f, 6f), platformMat);
            CreatePrimitive(PrimitiveType.Cube, "Platform_B", level.transform, new Vector3(12f, 1.5f, 10f), new Vector3(6f, 1f, 6f), platformMat);
            CreatePrimitive(PrimitiveType.Cube, "Platform_C", level.transform, new Vector3(16f, 2.5f, 14f), new Vector3(6f, 1f, 6f), platformMat);
            CreatePrimitive(PrimitiveType.Cube, "Box_A", level.transform, new Vector3(-9f, 1f, 4f), new Vector3(2f, 2f, 2f), obstacleMat);
            CreatePrimitive(PrimitiveType.Cube, "Box_B", level.transform, new Vector3(-13f, 1f, -6f), new Vector3(2f, 2f, 2f), obstacleMat);
            CreatePrimitive(PrimitiveType.Cylinder, "Pillar_A", level.transform, new Vector3(0f, 2f, 14f), new Vector3(2f, 2f, 2f), platformMat);
            CreatePrimitive(PrimitiveType.Cylinder, "Pillar_B", level.transform, new Vector3(-6f, 2f, -14f), new Vector3(2f, 2f, 2f), platformMat);

            GameObject spawns = new("SpawnPoints");
            Vector3[] spawnPositions =
            {
                new(-3f, 0.1f, -3f), new(3f, 0.1f, -3f), new(-3f, 0.1f, 3f), new(3f, 0.1f, 3f),
                new(-6f, 0.1f, 0f), new(6f, 0.1f, 0f)
            };
            for (int i = 0; i < spawnPositions.Length; i++)
            {
                GameObject point = new($"Spawn_{i}");
                point.transform.SetParent(spawns.transform, false);
                point.transform.position = spawnPositions[i];
                Vector3 toCenter = new(-spawnPositions[i].x, 0f, -spawnPositions[i].z);
                point.transform.rotation = toCenter.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(toCenter.normalized, Vector3.up)
                    : Quaternion.identity;
                point.AddComponent<PlayerSpawnPoint>();
            }

            CreateEventSystem();

            GameObject canvas = CreateCanvas("HudCanvas");

            TextMeshProUGUI hint = CreateUguiLabel(canvas.transform, "Hint", "", 22,
                TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(24f, 20f), new Vector2(-24f, 60f));

            TextMeshProUGUI players = CreateUguiLabel(canvas.transform, "Players", "Игроков: 0", 24,
                TextAlignmentOptions.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-260f, -60f), new Vector2(-24f, -20f));

            GameObject pausePanel = CreatePanel(canvas.transform, "PausePanel", new Vector2(520f, 380f));

            CreateUguiLabel(pausePanel.transform, "Title", "Пауза", 44, TextAlignmentOptions.Center,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -96f), new Vector2(-24f, -28f));

            Button resume = CreateButton(pausePanel.transform, "ResumeButton", "Продолжить", Accent,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(380f, 60f));
            Button toLobby = CreateButton(pausePanel.transform, "ReturnToLobbyButton", "Вернуться в лобби", Neutral,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(380f, 60f));
            Button leave = CreateButton(pausePanel.transform, "LeaveButton", "Выйти из сессии", new Color(0.45f, 0.20f, 0.22f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(380f, 60f));

            InGameHudView hud = canvas.AddComponent<InGameHudView>();
            SerializedObject so = new(hud);
            so.FindProperty("_pausePanel").objectReferenceValue = pausePanel;
            so.FindProperty("_resumeButton").objectReferenceValue = resume;
            so.FindProperty("_returnToLobbyButton").objectReferenceValue = toLobby;
            so.FindProperty("_leaveButton").objectReferenceValue = leave;
            so.FindProperty("_playersLabel").objectReferenceValue = players;
            so.FindProperty("_hintLabel").objectReferenceValue = hint;
            so.ApplyModifiedPropertiesWithoutUndo();

            pausePanel.SetActive(false);

            EditorSceneManager.SaveScene(scene, $"{Scenes}/{SceneCatalog.Game}.unity");
        }

        private static void UpdateBuildSettings()
        {
            List<EditorBuildSettingsScene> list = SceneCatalog.All
                .Select(name => $"{Scenes}/{name}.unity")
                .Where(File.Exists)
                .Select(path => new EditorBuildSettingsScene(path, true))
                .ToList();

            EditorBuildSettings.scenes = list.ToArray();
        }

        #endregion

        #region UI helpers

        private static void CreateUiCamera(string name, Color background)
        {
            GameObject go = new(name, typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.orthographic = true;
        }

        private static void CreateEventSystem()
        {
            GameObject go = new("EventSystem", typeof(EventSystem));
            go.AddComponent<InputSystemUIInputModule>();
        }

        private static GameObject CreateCanvas(string name)
        {
            GameObject go = new(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return go;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            go.GetComponent<Image>().color = Bg;
            return go;
        }

        private static TextMeshProUGUI CreateUguiLabel(Transform parent, string name, string text, int size,
            TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.93f, 0.94f, 0.96f);
            label.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            return label;
        }

        private static Button CreateButton(Transform parent, string name, string text, Color color,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = color;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            CreateUguiLabel(go.transform, "Label", text, 26, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return button;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string placeholder,
            Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.SetActive(false);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.10f);

            GameObject area = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            RectTransform areaRect = area.GetComponent<RectTransform>();
            areaRect.anchorMin = Vector2.zero;
            areaRect.anchorMax = Vector2.one;
            areaRect.offsetMin = new Vector2(14f, 6f);
            areaRect.offsetMax = new Vector2(-14f, -6f);

            TextMeshProUGUI placeholderLabel = CreateUguiLabel(area.transform, "Placeholder", placeholder, 24,
                TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            placeholderLabel.color = new Color(0.8f, 0.8f, 0.85f, 0.45f);

            TextMeshProUGUI textLabel = CreateUguiLabel(area.transform, "Text", string.Empty, 24,
                TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = areaRect;
            field.textComponent = textLabel;
            field.placeholder = placeholderLabel;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.targetGraphic = go.GetComponent<Image>();

            go.SetActive(true);
            return field;
        }

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;

            if (!keepCollider)
            {
                Collider collider = go.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            return go;
        }

        /// <summary>Ищет тип по полному имени во всех загруженных сборках домена.</summary>
        private static Type FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static GameObject SaveAndDestroy(GameObject go, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        #endregion
    }
}
