using Coop.App;
using Coop.Core.Sessions;
using Coop.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Coop.UI
{
    /// <summary>
    /// Главное меню.
    ///
    /// View сознательно «глупый»: он не создаёт сессию, не знает про Steam и FishNet.
    /// Его работа — превратить нажатие кнопки в вызов метода <see cref="SessionManager"/>
    /// и отрисовать состояние, о котором сообщил тот же SessionManager.
    ///
    /// Такое разделение (Humble View) даёт две практические вещи: логику сессии можно
    /// тестировать без сцены, а UI можно целиком переписать, не трогая сетевой код.
    /// </summary>
    public sealed class MainMenuView : MonoBehaviour
    {
        [FormerlySerializedAs("_hostButton")]
        [Header("Buttons")]
        [SerializeField] private Button hostButton;
        [FormerlySerializedAs("_joinButton")]
        [SerializeField] private Button joinButton;
        [FormerlySerializedAs("_quitButton")]
        [SerializeField] private Button quitButton;

        [FormerlySerializedAs("_titleLabel")]
        [Header("Labels")]
        [SerializeField] private TMP_Text titleLabel;
        [FormerlySerializedAs("_playerLabel")]
        [SerializeField] private TMP_Text playerLabel;
        [FormerlySerializedAs("_statusLabel")]
        [SerializeField] private TMP_Text statusLabel;

        [FormerlySerializedAs("_lanPanel")]
        [Header("LAN")]
        [Tooltip("Панель ручного подключения. Показывается только когда Steam недоступен.")]
        [SerializeField] private GameObject lanPanel;
        [FormerlySerializedAs("_addressField")]
        [SerializeField] private TMP_InputField addressField;

        private SessionManager session;

        private void OnEnable()
        {
            session = SessionServices.Session;

            hostButton.onClick.AddListener(OnHostClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            quitButton.onClick.AddListener(OnQuitClicked);

            if (session != null)
            {
                session.StateChanged += OnSessionStateChanged;
                session.Failed += OnSessionFailed;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Refresh();
        }

        private void OnDisable()
        {
            hostButton.onClick.RemoveListener(OnHostClicked);
            joinButton.onClick.RemoveListener(OnJoinClicked);
            quitButton.onClick.RemoveListener(OnQuitClicked);

            if (session != null)
            {
                session.StateChanged -= OnSessionStateChanged;
                session.Failed -= OnSessionFailed;
            }
        }

        private void Refresh()
        {
            bool steamMode = SessionServices.SteamMode;

            if (titleLabel != null)
                titleLabel.text = steamMode ? "Кооп-демо · Steam" : "Кооп-демо · LAN";

            if (playerLabel != null)
                playerLabel.text = SessionServices.LocalUser != null
                    ? $"Вы: {SessionServices.LocalUser.DisplayName}"
                    : "Вы: —";

            // Ручной ввод адреса нужен только там, где нет приглашений через оверлей.
            if (lanPanel != null)
                lanPanel.SetActive(!steamMode);

            if (addressField != null && string.IsNullOrEmpty(addressField.text) && SessionServices.Config != null)
                addressField.text = SessionServices.Config.LanAddress;

            SetStatus(steamMode
                ? "Создайте игру и пригласите друзей через оверлей Steam (Shift+Tab)."
                : $"Steam недоступен: {SessionServices.SteamUnavailableReason}");

            SetInteractable(session != null && session.State == SessionState.Offline);
        }

        private void OnHostClicked()
        {
            SetInteractable(false);
            SetStatus("Создаём лобби…");
            session?.Host();
        }

        private void OnJoinClicked()
        {
            string key = addressField != null ? addressField.text : string.Empty;

            SetInteractable(false);
            SetStatus("Подключаемся…");
            session?.Join(key);
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnSessionStateChanged(SessionState state)
        {
            SetInteractable(state == SessionState.Offline);

            switch (state)
            {
                case SessionState.Hosting:
                    SetStatus("Запускаем сервер…");
                    break;
                case SessionState.Connecting:
                    SetStatus("Подключаемся к хосту…");
                    break;
                case SessionState.Offline:
                    Refresh();
                    break;
            }
        }

        private void OnSessionFailed(string message) => SetStatus(message);

        private void SetStatus(string message)
        {
            if (statusLabel != null)
                statusLabel.text = message;
        }

        private void SetInteractable(bool value)
        {
            hostButton.interactable = value;
            joinButton.interactable = value && !SessionServices.SteamMode;
        }
    }
}
