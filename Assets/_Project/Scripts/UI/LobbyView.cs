using System.Collections.Generic;
using Coop.App;
using Coop.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Coop.UI
{
    /// <summary>
    /// Экран лобби: список игроков, готовность, приглашение друзей, старт матча.
    ///
    /// Весь список строится из <see cref="PlayerSessionRegistry"/> — то есть из сетевых
    /// объектов, реплицированных сервером. UI не хранит собственной копии состояния лобби
    /// и не пытается «угадать», кто готов: он перерисовывается по событию Changed.
    /// Это исключает целый класс багов «на экране одно, на сервере другое».
    /// </summary>
    public sealed class LobbyView : MonoBehaviour
    {
        [FormerlySerializedAs("_listRoot")]
        [Header("Player list")]
        [SerializeField] private RectTransform listRoot;
        [FormerlySerializedAs("_entryPrefab")]
        [SerializeField] private LobbyMemberEntry entryPrefab;

        [FormerlySerializedAs("_readyButton")]
        [Header("Buttons")]
        [SerializeField] private Button readyButton;
        [FormerlySerializedAs("_startButton")]
        [SerializeField] private Button startButton;
        [FormerlySerializedAs("_inviteButton")]
        [SerializeField] private Button inviteButton;
        [FormerlySerializedAs("_leaveButton")]
        [SerializeField] private Button leaveButton;

        [FormerlySerializedAs("_readyButtonLabel")]
        [Header("Labels")]
        [SerializeField] private TMP_Text readyButtonLabel;
        [FormerlySerializedAs("_hintLabel")]
        [SerializeField] private TMP_Text hintLabel;

        private readonly List<LobbyMemberEntry> entries = new();

        private void OnEnable()
        {
            readyButton.onClick.AddListener(OnReadyClicked);
            startButton.onClick.AddListener(OnStartClicked);
            inviteButton.onClick.AddListener(OnInviteClicked);
            leaveButton.onClick.AddListener(OnLeaveClicked);

            PlayerSessionRegistry.Changed += Redraw;
            SessionCoordinator.InstanceChanged += OnCoordinatorChanged;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Redraw();
        }

        private void OnDisable()
        {
            readyButton.onClick.RemoveListener(OnReadyClicked);
            startButton.onClick.RemoveListener(OnStartClicked);
            inviteButton.onClick.RemoveListener(OnInviteClicked);
            leaveButton.onClick.RemoveListener(OnLeaveClicked);

            PlayerSessionRegistry.Changed -= Redraw;
            SessionCoordinator.InstanceChanged -= OnCoordinatorChanged;
        }

        private void OnCoordinatorChanged(SessionCoordinator coordinator) => Redraw();

        private void Redraw()
        {
            IReadOnlyList<PlayerSession> players = PlayerSessionRegistry.All;

            EnsureEntryCount(players.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                bool used = i < players.Count;
                entries[i].gameObject.SetActive(used);

                if (used)
                    entries[i].Bind(players[i]);
            }

            PlayerSession local = PlayerSessionRegistry.Local;
            bool hasLocal = local != null;

            readyButton.interactable = hasLocal;
            if (readyButtonLabel != null)
                readyButtonLabel.text = hasLocal && local.IsReady ? "Не готов" : "Готов";

            // Кнопку старта видит только хост, и активна она лишь когда все готовы.
            bool isHost = hasLocal && local.IsSessionHost;
            startButton.gameObject.SetActive(isHost);
            startButton.interactable = isHost && PlayerSessionRegistry.AllReady();

            inviteButton.gameObject.SetActive(SessionServices.Session != null && SessionServices.Session.CanInvite);

            if (hintLabel != null)
            {
                hintLabel.text = isHost
                    ? "Пригласите друзей и нажмите «Начать», когда все будут готовы."
                    : "Ждём, пока хост начнёт матч.";
            }
        }

        /// <summary>
        /// Создаёт недостающие строки списка. Существующие переиспользуются, а не удаляются:
        /// пересоздание UI-иерархии каждый кадр — самый частый источник просадок в списках.
        /// </summary>
        private void EnsureEntryCount(int count)
        {
            while (entries.Count < count)
            {
                LobbyMemberEntry entry = Instantiate(entryPrefab, listRoot);
                entries.Add(entry);
            }
        }

        private void OnReadyClicked()
        {
            PlayerSession local = PlayerSessionRegistry.Local;
            if (local == null)
                return;

            // Клиент не меняет своё состояние сам: он просит об этом сервер,
            // а обратно приезжает уже реплицированное значение SyncVar.
            local.SetReady(!local.IsReady);
        }

        private void OnStartClicked() => SessionCoordinator.Instance?.RequestStartMatchServerRpc();

        private void OnInviteClicked() => SessionServices.Session?.InviteFriends();

        private void OnLeaveClicked() => SessionServices.Session?.Leave();
    }
}
