using UnityEngine;
using UnityEngine.Serialization;

namespace Coop.Gameplay
{
    /// <summary>
    /// Камера от третьего лица, живущая в игровой сцене (а не на префабе персонажа).
    ///
    /// Почему так:
    /// * камера и AudioListener должны существовать в единственном экземпляре. Если положить
    ///   их на префаб персонажа, у каждого подключённого игрока в сцене окажется своя камера,
    ///   и Unity будет ругаться на несколько AudioListener;
    /// * камера — это чисто визуальная вещь. Ей нечего делать в сетевом объекте, который
    ///   реплицируется и участвует в предсказании.
    ///
    /// Персонаж владельца сам находит риг при спавне и «представляется» ему.
    /// Рыскание камеры при этом — часть ввода: оно уходит на сервер внутри
    /// <see cref="PlayerMotor.MoveData"/>, чтобы сервер повернул персонажа так же, как клиент.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        [FormerlySerializedAs("_pivotOffset")]
        [Header("Follow")]
        [SerializeField] private Vector3 pivotOffset = new(0f, 1.6f, 0f);
        [FormerlySerializedAs("_distance")]
        [SerializeField] private float distance = 6f;
        [FormerlySerializedAs("_followLerp")]
        [SerializeField] private float followLerp = 18f;

        [FormerlySerializedAs("_sensitivity")]
        [Header("Orbit")]
        [SerializeField] private float sensitivity = 0.12f;
        [FormerlySerializedAs("_minPitch")]
        [SerializeField] private float minPitch = -20f;
        [FormerlySerializedAs("_maxPitch")]
        [SerializeField] private float maxPitch = 65f;
        [FormerlySerializedAs("_defaultPitch")]
        [SerializeField] private float defaultPitch = 18f;

        [FormerlySerializedAs("_obstructionMask")]
        [Header("Collision")]
        [Tooltip("Слои, сквозь которые камера не должна проходить.")]
        [SerializeField] private LayerMask obstructionMask = ~0;
        [FormerlySerializedAs("_collisionRadius")]
        [SerializeField] private float collisionRadius = 0.25f;

        private Transform target;
        private PlayerInputReader input;
        private float pitch;

        /// <summary>Текущее рыскание камеры в градусах. Читается контроллером движения.</summary>
        public float Yaw { get; private set; }

        private void Awake() => pitch = defaultPitch;

        /// <summary>Привязать камеру к персонажу локального игрока.</summary>
        public void SetTarget(Transform target, PlayerInputReader input)
        {
            this.target = target;
            this.input = input;
            Yaw = target != null ? target.eulerAngles.y : 0f;
        }

        /// <summary>Отвязаться, если это всё ещё наш персонаж (защита от гонки при респавне).</summary>
        public void ClearTarget(Transform target)
        {
            if (this.target != target)
                return;

            this.target = null;
            input = null;
        }

        /// <summary>
        /// Камера обновляется в LateUpdate, после того как все объекты уже подвинулись за кадр.
        /// Иначе она будет отставать на кадр и картинка начнёт дрожать.
        /// </summary>
        private void LateUpdate()
        {
            if (target == null)
                return;

            if (input != null)
            {
                Vector2 look = input.Look;
                Yaw += look.x * sensitivity;
                pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);
            }

            Quaternion rotation = Quaternion.Euler(pitch, Yaw, 0f);
            Vector3 pivot = target.position + pivotOffset;
            Vector3 desired = pivot - rotation * Vector3.forward * distance;

            // Не даём камере уехать за стену: если между точкой обзора и камерой есть геометрия,
            // подтягиваем камеру ближе.
            if (Physics.SphereCast(pivot, collisionRadius, (desired - pivot).normalized,
                    out RaycastHit hit, distance, obstructionMask, QueryTriggerInteraction.Ignore))
            {
                desired = pivot + (desired - pivot).normalized * hit.distance;
            }

            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            transform.rotation = rotation;
        }
    }
}
