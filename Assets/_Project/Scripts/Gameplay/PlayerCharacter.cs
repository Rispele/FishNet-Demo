using Coop.Networking;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Serialization;

namespace Coop.Gameplay
{
    /// <summary>
    /// Связывает персонажа в игровой сцене с профилем игрока (<see cref="PlayerSession"/>).
    ///
    /// Персонаж намеренно не хранит ни имени, ни цвета собственными SyncVar'ами: эти данные
    /// уже реплицированы в профиле, который живёт всю сессию. Дублировать их значило бы
    /// платить трафиком дважды и получить два источника правды.
    ///
    /// Профиль может прийти позже персонажа (спавн двух объектов — это две разные посылки),
    /// поэтому компонент подписывается на изменения реестра и повторяет попытку связаться.
    /// Такая «поздняя привязка» — типовая задача в сетевом коде: порядок доставки объектов
    /// не гарантирован, и код обязан быть к этому готов.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerCharacter : NetworkBehaviour
    {
        [FormerlySerializedAs("_appearance")]
        [SerializeField] private PlayerAppearance appearance;
        [FormerlySerializedAs("_nameplate")]
        [SerializeField] private PlayerNameplate nameplate;

        [FormerlySerializedAs("_hideOwnNameplate")]
        [Tooltip("Ник локального игрока показывать не нужно — он и так знает, кто он.")]
        [SerializeField] private bool hideOwnNameplate = true;

        private PlayerSession session;

        public override void OnStartClient()
        {
            PlayerSessionRegistry.Changed += TryBindSession;
            TryBindSession();
        }

        public override void OnStopClient()
        {
            PlayerSessionRegistry.Changed -= TryBindSession;
        }

        private void TryBindSession()
        {
            if (session != null || Owner == null)
                return;

            session = PlayerSessionRegistry.FindByClientId(Owner.ClientId);
            if (session == null)
                return;

            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            Color color = PlayerPalette.Get(session.ColorIndex);

            if (appearance != null)
                appearance.SetColor(color);

            if (nameplate == null)
                return;

            bool hide = hideOwnNameplate && IsOwner;
            nameplate.gameObject.SetActive(!hide);

            if (!hide)
                nameplate.SetText(session.DisplayName, color);
        }
    }
}
