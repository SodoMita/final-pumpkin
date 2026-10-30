using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Tank controls, the Resident Evil 1-3 / Signalis way:
    /// the character only turns on its own Y axis and walks along its facing.
    /// The camera never steers it.
    ///
    /// This class is the only place that knows about the control scheme;
    /// replacing Update() with camera-relative twin-stick movement would not
    /// touch anything else in the project.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerTankController : MonoBehaviour
    {
        public Transform Visual;
        public Transform Feet;

        [Header("Movement")]
        public float Acceleration = 14f;
        public float BackpedalFactor = 0.6f;
        public float Gravity = 20f;
        public float StepInterval = 0.32f;

        [Header("Feel")]
        public float RunHoldTime = 0.25f;

        CharacterController _controller;
        float _speed;
        float _vertical;
        float _stepPhase;
        float _runTimer;

        public bool Frozen { get; set; }
        public bool Aiming { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        void Update()
        {
            float delta = Time.deltaTime;
            if (Frozen || delta <= 0f)
            {
                _speed = Mathf.MoveTowards(_speed, 0f, Acceleration * delta);
                AnimateFeet(0f);
                return;
            }

            ControlSettings settings = SettingsService.Instance.Data.controls;
            InputService input = InputService.Instance;

            Aiming = input.GetHeld(GameAction.Aim);

            // --- Turning: always available, independent of movement ----------
            float turn = input.TurnAxis;
            if (settings.invertTurn) turn = -turn;
            float turnSpeed = Aiming ? settings.aimTurnSpeed : settings.turnSpeed;
            transform.Rotate(0f, turn * turnSpeed * Mathf.Rad2Deg * delta, 0f, Space.Self);

            // --- Walking: straight along the facing direction ----------------
            float move = input.MoveAxis;
            if (Aiming) move = 0f; // aiming roots the character, like drawing a weapon

            _runTimer = input.GetHeld(GameAction.Run) ? _runTimer + delta : 0f;
            bool running = settings.simultaneousRun && move > 0.01f && _runTimer >= RunHoldTime;

            float speed = running ? settings.runSpeed : settings.walkSpeed;
            if (move < 0f) speed *= BackpedalFactor;

            _speed = Mathf.MoveTowards(_speed, speed * move, Acceleration * delta);

            Vector3 motion = transform.forward * _speed;
            _vertical = _controller.isGrounded ? -1f : _vertical - Gravity * delta;
            motion.y = _vertical;
            _controller.Move(motion * delta);

            AnimateFeet(Mathf.Abs(turn) * 2f + Mathf.Abs(_speed));
        }

        /// Cosmetic only: bobs a cylinder around so movement reads on screen.
        void AnimateFeet(float activity)
        {
            if (Feet == null) return;
            if (activity < 0.05f)
            {
                _stepPhase = Mathf.MoveTowards(_stepPhase, 0f, Time.deltaTime * 4f);
                Feet.localPosition = new Vector3(0f, 0.12f, Mathf.MoveTowards(Feet.localPosition.z, 0f, Time.deltaTime));
                Feet.localRotation = Quaternion.Slerp(Feet.localRotation, Quaternion.identity, Time.deltaTime * 6f);
                return;
            }

            _stepPhase += Time.deltaTime / Mathf.Max(StepInterval, 0.05f);
            float swing = Mathf.Sin(_stepPhase * Mathf.PI * 2f) * 0.12f * Mathf.Min(activity, 1.5f);
            Feet.localPosition = new Vector3(0f, 0.12f + Mathf.Abs(swing) * 0.15f, swing);
            Feet.localRotation = Quaternion.Euler(0f, swing * 70f, 0f);
        }
    }
}
