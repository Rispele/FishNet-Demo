using Coop.Core.Diagnostics;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;
using UnityEngine.Serialization;

namespace Coop.Gameplay
{
    /// <summary>
    /// Серверно-авторитетное движение персонажа с клиентским предсказанием (CSP)
    /// и реконсиляцией — это «золотой стандарт» сетевого движения в экшенах.
    ///
    /// ───────── Как это работает ─────────
    ///
    /// Наивная схема «клиент двигается сам и шлёт позицию» позволяет тривиальный чит:
    /// клиент просто присылает любую координату. Схема «клиент шлёт ввод, ждёт ответ сервера»
    /// честная, но добавляет к управлению полный round-trip (60–150 мс), что ощущается как лаг.
    ///
    /// CSP объединяет достоинства обеих:
    ///
    /// 1. Каждый сетевой тик владелец собирает <see cref="MoveData"/> (ввод, не позицию)
    ///    и вызывает метод, помеченный [Replicate].
    /// 2. FishNet делает две вещи одновременно: локально выполняет метод (клиент видит
    ///    отклик мгновенно, ноль задержки) и отправляет ввод серверу.
    /// 3. Сервер выполняет ТОТ ЖЕ метод с тем же вводом на том же номере тика. Именно
    ///    результат сервера считается истиной.
    /// 4. Раз в тик сервер шлёт назад состояние — <see cref="MotorState"/> ([Reconcile]).
    ///    Клиент сравнивает его со своим состоянием в том же тике. Совпало — ничего не делает.
    ///    Не совпало (лаг, потеря пакета, коллизия с другим игроком) — откатывается
    ///    к серверному состоянию и заново проигрывает все свои неподтверждённые вводы.
    ///
    /// Отсюда два жёстких правила, которые нельзя нарушать:
    ///
    /// * В [Replicate] попадает ВСЁ, что влияет на движение, и только детерминированный код.
    ///   Никаких Time.deltaTime (только TimeManager.TickDelta), никаких Random, никакого чтения
    ///   «живого» ввода: метод вызывается повторно при пересимуляции прошлых тиков.
    /// * В [Reconcile] попадает ВСЁ изменяемое состояние симуляции (позиция, вертикальная
    ///   скорость, поворот). Забытое поле = рассинхрон, который проявляется как дрожание.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : TickNetworkBehaviour
    {
        #region Replicate / Reconcile data

        /// <summary>
        /// Ввод за один сетевой тик. Передаётся ненадёжным каналом: потерянный пакет ввода
        /// дешевле «догадать», чем ждать его переотправки.
        ///
        /// Здесь лежит ввод, а не результат: сервер обязан сам вычислить перемещение,
        /// иначе никакой авторитетности нет.
        /// </summary>
        public struct MoveData : IReplicateData
        {
            /// <summary>Ввод направления в осях экрана: X — вбок, Y — вперёд.</summary>
            public Vector2 move;

            /// <summary>
            /// Рыскание камеры владельца. Передаётся, чтобы сервер преобразовал ввод
            /// в мировое направление точно так же, как это сделал клиент.
            /// </summary>
            public float yaw;

            public bool sprint;
            public bool jump;

            private uint tick;

            public MoveData(Vector2 move, float yaw, bool sprint, bool jump)
            {
                this.move = move;
                this.yaw = yaw;
                this.sprint = sprint;
                this.jump = jump;
                tick = 0;
            }

            // Номер тика проставляет FishNet — вручную его трогать не нужно.
            public uint GetTick() => tick;
            public void SetTick(uint value) => tick = value;

            /// <summary>Освобождение ссылочных данных. Здесь нечего освобождать: структура — value type.</summary>
            public void Dispose() { }
        }

        /// <summary>
        /// Полный снимок состояния симуляции на конец тика. Это то, чем сервер
        /// «поправляет» клиента. Всё, что меняется внутри Replicate, обязано быть здесь.
        /// </summary>
        public struct MotorState : IReconcileData
        {
            public Vector3 position;
            public float yaw;
            public float verticalVelocity;

            private uint tick;

            public MotorState(Vector3 position, float yaw, float verticalVelocity)
            {
                this.position = position;
                this.yaw = yaw;
                this.verticalVelocity = verticalVelocity;
                tick = 0;
            }

            public uint GetTick() => tick;
            public void SetTick(uint value) => tick = value;
            public void Dispose() { }
        }

        #endregion

        [FormerlySerializedAs("_walkSpeed")]
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4.5f;
        [FormerlySerializedAs("_sprintSpeed")]
        [SerializeField] private float sprintSpeed = 7.5f;
        [FormerlySerializedAs("_jumpSpeed")]
        [SerializeField] private float jumpSpeed = 6f;
        [FormerlySerializedAs("_gravity")]
        [SerializeField] private float gravity = -20f;
        [FormerlySerializedAs("_turnSpeedDegrees")]
        [SerializeField] private float turnSpeedDegrees = 720f;

        [FormerlySerializedAs("_reconcileTickInterval")]
        [Header("Reconcile")]
        [Tooltip("Отправлять состояние на реконсиляцию не каждый тик, а раз в N тиков. " +
                 "1 = максимальная точность, больше = меньше трафика.")]
        [Range(1, 5)]
        [SerializeField] private int reconcileTickInterval = 1;

        private const string LogContext = "PlayerMotor";

        private CharacterController controller;
        private PlayerInputReader input;
        private PlayerCameraRig cameraRig;

        /// <summary>Часть состояния симуляции: обязана попадать в <see cref="MotorState"/>.</summary>
        private float verticalVelocity;

        /// <summary>Последний ввод, реально пришедший от владельца. Нужен для экстраполяции.</summary>
        private MoveData lastKnownInput;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();

            // Просим FishNet вызывать нас на тике (симуляция) и после тика (сборка реконсиляции).
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        /// <summary>
        /// Страховка от «немого» бага конфигурации.
        ///
        /// Пересылка предсказанных состояний наблюдателям включается галкой Enable Prediction
        /// на NetworkObject: FishNet считает EnableStateForwarding как
        /// (_enablePrediction &amp;&amp; _enableStateForwarding). Если галка снята, владелец и сервер
        /// продолжают двигаться абсолютно корректно, а ОСТАЛЬНЫЕ клиенты не получают ни одного
        /// состояния — чужой персонаж просто стоит на месте. Ошибки при этом не возникает
        /// нигде, поэтому проверяем условие явно и громко.
        /// </summary>
        public override void OnStartNetwork()
        {
            if (!NetworkObject.EnablePrediction)
            {
                CoopLog.Error(LogContext,
                    $"На префабе {name} у NetworkObject выключен Enable Prediction. " +
                    "Владелец будет двигаться, но остальные клиенты увидят его неподвижным: " +
                    "FishNet не пересылает наблюдателям replicate/reconcile-состояния.", this);
            }
        }

        public override void OnStartClient()
        {
            if (!IsOwner)
                return;

            // Ввод читает только владелец: чужие персонажи двигаются по данным с сервера.
            input.EnableInput();

            cameraRig = FindAnyObjectByType<PlayerCameraRig>();
            if (cameraRig != null)
                cameraRig.SetTarget(transform, input);
        }

        public override void OnStopClient()
        {
            if (!IsOwner)
                return;

            input.DisableInput();

            if (cameraRig != null)
                cameraRig.ClearTarget(transform);
        }

        /// <summary>
        /// Тик сети. Здесь и только здесь собирается ввод и запускается симуляция.
        /// </summary>
        protected override void TimeManager_OnTick()
        {
            Simulate(BuildMoveData());
        }

        /// <summary>
        /// Пост-тик: физика за этот тик уже отработала, значит состояние окончательное
        /// и его можно упаковать в реконсиляцию.
        /// </summary>
        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        /// <summary>
        /// Собирает ввод владельца. У всех остальных возвращает default: сервер получит
        /// настоящий ввод по сети, а наблюдатели будут экстраполировать (см. Simulate).
        /// </summary>
        private MoveData BuildMoveData()
        {
            if (!IsOwner)
                return default;

            float yaw = cameraRig != null ? cameraRig.Yaw : transform.eulerAngles.y;
            return new MoveData(input.Move, yaw, input.SprintHeld, input.ConsumeJump());
        }

        /// <summary>
        /// Вызывается FishNet'ом: и на сервере, и на клиенте, включая повторные вызовы
        /// во время реконсиляции.
        /// </summary>
        public override void CreateReconcile()
        {
            // Экономия трафика: сервер шлёт состояние не каждый тик. Клиент при этом
            // всё равно собирает своё состояние — оно используется как локальный ориентир.
            if (IsServerStarted && reconcileTickInterval > 1 &&
                TimeManager.LocalTick % (uint)reconcileTickInterval != 0)
                return;

            MotorState state = new(transform.position, transform.eulerAngles.y, verticalVelocity);
            PerformReconcile(state);
        }

        /// <summary>
        /// Симуляция одного тика. Детерминированная функция от (состояние, ввод).
        /// </summary>
        [Replicate]
        private void Simulate(MoveData data, ReplicateState state = ReplicateState.Invalid,
            Channel channel = Channel.Unreliable)
        {
            // Всегда TickDelta, никогда Time.deltaTime: во время реконсиляции за один кадр
            // может проигрываться десяток тиков, и Time.deltaTime дал бы неверный результат.
            float delta = (float)TimeManager.TickDelta;

            data = PredictInputForSpectators(data, state);

            bool grounded = controller.isGrounded;

            if (grounded && verticalVelocity < 0f)
            {
                // Небольшая отрицательная скорость прижимает контроллер к земле,
                // иначе isGrounded начинает «моргать» на склонах и стыках коллайдеров.
                verticalVelocity = -2f;
            }

            if (data.jump && grounded)
                verticalVelocity = jumpSpeed;

            verticalVelocity += gravity * delta;

            Vector3 direction = Quaternion.Euler(0f, data.yaw, 0f) * new Vector3(data.move.x, 0f, data.move.y);
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            float speed = data.sprint ? sprintSpeed : walkSpeed;

            Vector3 velocity = direction * speed;
            velocity.y = verticalVelocity;

            controller.Move(velocity * delta);

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegrees * delta);
            }
        }

        /// <summary>
        /// Приведение состояния к серверному и повтор неподтверждённых тиков.
        ///
        /// Тело метода выглядит примитивно — «присвоить поля». Вся сложность (хранение
        /// истории вводов, определение расхождения, повторный прогон Simulate) реализована
        /// в FishNet и запускается автоматически после выхода из этого метода.
        /// </summary>
        [Reconcile]
        private void PerformReconcile(MotorState state, Channel channel = Channel.Reliable)
        {
            // CharacterController перехватывает запись в transform.position, поэтому
            // на время телепорта его нужно выключить — иначе позиция «не приедет».
            controller.enabled = false;
            transform.position = state.position;
            transform.rotation = Quaternion.Euler(0f, state.yaw, 0f);
            controller.enabled = true;

            verticalVelocity = state.verticalVelocity;
        }

        /// <summary>
        /// Экстраполяция ввода для чужих персонажей.
        ///
        /// Клиент видит чужие вводы с задержкой в сетевой RTT. Если ничего не делать,
        /// чужие персонажи будут двигаться рывками. Поэтому для тиков, данных по которым
        /// физически ещё не может быть (ReplicateState.*Future*), мы повторяем последний
        /// известный ввод — но не более одного тика вперёд, чтобы ошибка предсказания
        /// оставалась незаметной.
        ///
        /// Прыжок при этом не повторяется: он одноразовый, и «залипший» прыжок выглядел бы
        /// куда хуже, чем чуть более поздний.
        /// </summary>
        private MoveData PredictInputForSpectators(MoveData data, ReplicateState state)
        {
            // Сервер всегда знает настоящий ввод, владелец — тем более.
            if (IsServerStarted || IsOwner)
                return data;

            if (state.ContainsTicked())
            {
                lastKnownInput = data;
                return data;
            }

            if (!state.IsFuture())
                return data;

            if (data.GetTick() - lastKnownInput.GetTick() > 1)
                return default;

            MoveData predicted = lastKnownInput;
            predicted.jump = false;
            return predicted;
        }
    }
}
