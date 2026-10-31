using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Plays the three clips baked into the Anny zombies: Shamble (walk cycle,
    /// loops), Idle (sway, loops) and Lunge (one-shot grab).
    ///
    /// The driver is deliberately dumb - no navmesh, no AI, no combat. It keeps
    /// a zombie visibly alive by alternating Shamble and Idle, throwing a Lunge
    /// every so often, and slowly turning towards a target when there is one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZombieAnimationDriver : MonoBehaviour
    {
        public enum State
        {
            Shamble,
            Idle,
            Lunge,
        }

        const float FadeDuration = 0.25f;

        static readonly float MinShambleSeconds = 3.5f;
        static readonly float MaxShambleSeconds = 7f;
        static readonly float MinIdleSeconds = 1.2f;
        static readonly float MaxIdleSeconds = 2.8f;

        [SerializeField] float turnSpeed = 40f;
        [SerializeField] float noticeDistance = 14f;

        Animation _animation;
        string _currentClip;
        State _state = State.Shamble;
        float _stateTimer;
        bool _bound;

        /// <summary>Optional: the zombie faces this transform while it is near.</summary>
        public Transform Target { get; set; }

        public State CurrentState
        {
            get { return _state; }
        }

        /// <summary>Name of the clip this driver last started, or null before Bind.</summary>
        public string CurrentClip
        {
            get { return _currentClip; }
        }

        public Animation Animation
        {
            get { return _animation; }
        }

        public float TurnSpeed
        {
            get { return turnSpeed; }
            set { turnSpeed = value; }
        }

        public float NoticeDistance
        {
            get { return noticeDistance; }
            set { noticeDistance = value; }
        }

        /// <summary>Attaches the driver to a legacy Animation holding the clips.</summary>
        public void Bind(Animation animation)
        {
            _animation = animation;
            _bound = animation != null;
            if (!_bound) return;

            // Stagger the start so a group of zombies never moves in lockstep.
            _stateTimer = Random.Range(0.2f, 2.5f);
            _state = State.Shamble;
            PlayClip(ZombieFactory.ShambleClipName, FadeDuration);
        }

        public void PlayShamble()
        {
            EnterState(State.Shamble, Random.Range(MinShambleSeconds, MaxShambleSeconds));
        }

        public void PlayIdle()
        {
            EnterState(State.Idle, Random.Range(MinIdleSeconds, MaxIdleSeconds));
        }

        public void PlayLunge()
        {
            EnterState(State.Lunge, LungeDuration());
        }

        void Update()
        {
            if (!_bound) return;

            FaceTarget();

            _stateTimer -= Time.deltaTime;
            if (_stateTimer > 0f) return;

            switch (_state)
            {
                case State.Shamble:
                {
                    // A lunge is a one-shot: when its clip ends the driver folds
                    // straight back into the walk instead of freezing mid-reach.
                    if (Random.value < 0.25f) EnterState(State.Lunge, LungeDuration());
                    else EnterState(State.Idle, Random.Range(MinIdleSeconds, MaxIdleSeconds));
                    break;
                }
                case State.Idle:
                    EnterState(State.Shamble, Random.Range(MinShambleSeconds, MaxShambleSeconds));
                    break;
                default:
                    EnterState(State.Shamble, Random.Range(MinShambleSeconds, MaxShambleSeconds));
                    break;
            }
        }

        void FaceTarget()
        {
            if (Target == null || turnSpeed <= 0f) return;

            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude > noticeDistance * noticeDistance) return;

            Quaternion wanted = Quaternion.LookRotation(toTarget, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * Time.deltaTime);
        }

        void EnterState(State state, float duration)
        {
            _state = state;
            _stateTimer = duration;

            switch (state)
            {
                case State.Shamble:
                    PlayClip(ZombieFactory.ShambleClipName, FadeDuration);
                    break;
                case State.Idle:
                    PlayClip(ZombieFactory.IdleClipName, FadeDuration);
                    break;
                case State.Lunge:
                    PlayClip(ZombieFactory.LungeClipName, FadeDuration / 2f);
                    break;
            }
        }

        void PlayClip(string clipName, float fade)
        {
            if (_animation == null || string.IsNullOrEmpty(clipName)) return;
            if (_animation.GetClip(clipName) == null) return;
            if (_currentClip == clipName && _animation.isPlaying) return;
            _animation.CrossFade(clipName, fade);
            // CrossFade leaves Animation.clip alone, so remember what we asked for.
            _currentClip = clipName;
        }

        float LungeDuration()
        {
            AnimationClip clip = _animation != null ? _animation.GetClip(ZombieFactory.LungeClipName) : null;
            // Clamp so a missing or zero-length clip cannot lock the state machine.
            return clip != null && clip.length > 0.05f ? clip.length : 1.2f;
        }

        /// <summary>Convenience for tests and for spawn code that wants a fixed rhythm.</summary>
        public void ForceState(State state, float duration)
        {
            EnterState(state, duration);
        }
    }
}
