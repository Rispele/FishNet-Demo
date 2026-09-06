using Coop.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Coop.UI
{
    /// <summary>
    /// Строка списка игроков в лобби. Один экземпляр на игрока, переиспользуется
    /// пулом внутри <see cref="LobbyView"/>.
    /// </summary>
    public sealed class LobbyMemberEntry : MonoBehaviour
    {
        [FormerlySerializedAs("_colorSwatch")]
        [SerializeField] private Image colorSwatch;
        [FormerlySerializedAs("_nameLabel")]
        [SerializeField] private TMP_Text nameLabel;
        [FormerlySerializedAs("_statusLabel")]
        [SerializeField] private TMP_Text statusLabel;

        public void Bind(PlayerSession session)
        {
            if (session == null)
                return;

            if (colorSwatch != null)
                colorSwatch.color = PlayerPalette.Get(session.ColorIndex);

            if (nameLabel != null)
            {
                string suffix = session.IsSessionHost ? " (хост)" : string.Empty;
                string you = session.IsLocal ? " · вы" : string.Empty;
                nameLabel.text = $"{session.DisplayName}{suffix}{you}";
            }

            if (statusLabel != null)
            {
                statusLabel.text = session.IsReady ? "готов" : "не готов";
                statusLabel.color = session.IsReady ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.85f, 0.55f, 0.3f);
            }
        }
    }
}
