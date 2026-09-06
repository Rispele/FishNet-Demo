using Coop.App;
using Coop.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Coop.UI
{
    /// <summary>
    /// HUD игровой сцены: подсказка по управлению, счётчик игроков и меню паузы.
    /// </summary>
    public sealed class InGameHudView : MonoBehaviour
    {
        [FormerlySerializedAs("_pausePanel")]
        [SerializeField] private GameObject pausePanel;
        [FormerlySerializedAs("_resumeButton")]
        [SerializeField] private Button resumeButton;
        [FormerlySerializedAs("_returnToLobbyButton")]
        [SerializeField] private Button returnToLobbyButton;
        [FormerlySerializedAs("_leaveButton")]
        [SerializeField] private Button leaveButton;
        [FormerlySerializedAs("_playersLabel")]
        [SerializeField] private TMP_Text playersLabel;
        [FormerlySerializedAs("_hintLabel")]
        [SerializeField] private TMP_Text hintLabel;

        private bool paused;

        private void OnEnable()
        {
            resumeButton.onClick.AddListener(() => SetPaused(false));
            returnToLobbyButton.onClick.AddListener(OnReturnToLobbyClicked);
            leaveButton.onClick.AddListener(OnLeaveClicked);

            PlayerSessionRegistry.Changed += RefreshPlayers;

            if (hintLabel != null)
                hintLabel.text = "WASD — движение · Shift — бег · Space — прыжок · ЛКМ+мышь — камера · Esc — меню";

            SetPaused(false);
            RefreshPlayers();
        }

        private void OnDisable()
        {
            resumeButton.onClick.RemoveAllListeners();
            returnToLobbyButton.onClick.RemoveAllListeners();
            leaveButton.onClick.RemoveAllListeners();

            PlayerSessionRegistry.Changed -= RefreshPlayers;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            // Новая Input System: старый UnityEngine.Input в этом проекте отключён.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                SetPaused(!paused);
        }

        private void SetPaused(bool paused)
        {
            this.paused = paused;

            if (pausePanel != null)
                pausePanel.SetActive(paused);

            // Курсор прячем только в игре: в паузе он нужен для кнопок.
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;

            // Кнопка возврата в лобби есть только у хоста: только он может менять сцену сессии.
            PlayerSession local = PlayerSessionRegistry.Local;
            returnToLobbyButton.gameObject.SetActive(local != null && local.IsSessionHost);
        }

        private void RefreshPlayers()
        {
            if (playersLabel != null)
                playersLabel.text = $"Игроков: {PlayerSessionRegistry.All.Count}";
        }

        private void OnReturnToLobbyClicked() => SessionCoordinator.Instance?.RequestReturnToLobbyServerRpc();

        private void OnLeaveClicked() => SessionServices.Session?.Leave();
    }
}
