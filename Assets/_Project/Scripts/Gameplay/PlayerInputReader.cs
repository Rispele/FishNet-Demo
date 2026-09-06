using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Coop.Gameplay
{
    /// <summary>
    /// Единственная точка чтения ввода игрока.
    ///
    /// Зачем выделять отдельный компонент, а не читать Input прямо в контроллере движения:
    /// * контроллер движения выполняется в тиках сети и вызывается повторно во время
    ///   реконсиляции (пересимуляции прошлого). Читать «живой» ввод в этот момент нельзя —
    ///   нужно использовать тот, что был записан в конкретном тике;
    /// * ввод должен читаться ТОЛЬКО у владельца объекта. Чужой персонаж не должен реагировать
    ///   на нашу клавиатуру;
    /// * так ввод легко подменить (боты, реплеи, автотесты).
    ///
    /// Компонент включается вручную — см. <see cref="PlayerMotor"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [FormerlySerializedAs("_actionsAsset")]
        [Tooltip("Asset с Input Actions. Используется карта 'Player'.")]
        [SerializeField] private InputActionAsset actionsAsset;

        private InputActionAsset runtimeAsset;
        private InputAction move;
        private InputAction look;
        private InputAction jump;
        private InputAction sprint;

        /// <summary>Направление движения в локальных осях игрока: X — вправо, Y — вперёд.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>Дельта взгляда за кадр (мышь/стик).</summary>
        public Vector2 Look { get; private set; }

        /// <summary>Удерживается ли ускорение.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>
        /// Прыжок — «одноразовый» ввод: он происходит в конкретном кадре, а тик сети
        /// наступает реже, чем кадр. Поэтому нажатие накапливается до ближайшего тика
        /// и сбрасывается методом <see cref="ConsumeJump"/>.
        /// </summary>
        private bool jumpBuffered;

        public bool IsEnabled { get; private set; }

        private void Awake()
        {
            if (actionsAsset == null)
            {
                Debug.LogError($"{nameof(PlayerInputReader)}: не назначен InputActionAsset.", this);
                enabled = false;
                return;
            }

            // Клонируем asset: экземпляр действий не должен быть общим между объектами,
            // иначе включение ввода у одного персонажа затронет остальных.
            runtimeAsset = Instantiate(actionsAsset);

            InputActionMap map = runtimeAsset.FindActionMap("Player", throwIfNotFound: true);
            move = map.FindAction("Move", throwIfNotFound: true);
            look = map.FindAction("Look", throwIfNotFound: true);
            jump = map.FindAction("Jump", throwIfNotFound: true);
            sprint = map.FindAction("Sprint", throwIfNotFound: true);
        }

        private void OnDestroy()
        {
            if (runtimeAsset != null)
                Destroy(runtimeAsset);
        }

        /// <summary>Включает чтение ввода. Вызывается только для владельца персонажа.</summary>
        public void EnableInput()
        {
            if (IsEnabled || runtimeAsset == null)
                return;

            runtimeAsset.FindActionMap("Player").Enable();
            IsEnabled = true;
        }

        public void DisableInput()
        {
            if (!IsEnabled || runtimeAsset == null)
                return;

            runtimeAsset.FindActionMap("Player").Disable();
            IsEnabled = false;
            Move = Vector2.zero;
            Look = Vector2.zero;
            SprintHeld = false;
            jumpBuffered = false;
        }

        private void Update()
        {
            if (!IsEnabled)
                return;

            Move = move.ReadValue<Vector2>();
            Look = look.ReadValue<Vector2>();
            SprintHeld = sprint.IsPressed();

            if (jump.WasPressedThisFrame())
                jumpBuffered = true;
        }

        /// <summary>Забирает накопленное нажатие прыжка и сбрасывает буфер.</summary>
        public bool ConsumeJump()
        {
            bool jumped = jumpBuffered;
            jumpBuffered = false;
            return jumped;
        }
    }
}
