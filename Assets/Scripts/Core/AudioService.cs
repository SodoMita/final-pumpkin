using UnityEngine;

namespace Foundation
{
    public enum AudioBus
    {
        Music = 0,
        Sfx = 1,
        Voice = 2,
    }

    /// <summary>
    /// Four-channel audio without mixer assets: every sound is played through a
    /// pooled AudioSource whose volume is (bus volume * master volume). Simple,
    /// asset-free, and enough for a foundation.
    /// </summary>
    public class AudioService : MonoBehaviour
    {
        public static AudioService Instance { get; private set; }

        const int PoolSize = 8;

        AudioSource[] _pool;
        int _next;
        AudioSettings _settings = new AudioSettings();
        bool _focused = true;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _pool[i] = source;
            }
        }

        void Update()
        {
            if (!_settings.muteWhenUnfocused) return;
            bool focused = Application.isFocused;
            if (focused == _focused) return;
            _focused = focused;
            ApplyVolumes(_settings);
        }

        public void ApplyVolumes(AudioSettings settings)
        {
            _settings = settings;
            bool muted = settings.muteWhenUnfocused && !(Application.isFocused || !Application.isEditor);
            AudioListener.volume = muted ? 0f : Mathf.Clamp01(settings.master);
        }

        public float BusVolume(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Music: return Mathf.Clamp01(_settings.music);
                case AudioBus.Voice: return Mathf.Clamp01(_settings.voice);
                default: return Mathf.Clamp01(_settings.sfx);
            }
        }

        /// Plays a one-shot on the given bus. Returns the source so callers can
        /// configure spatial blend, pitch or loop before the clip finishes.
        public AudioSource Play(AudioClip clip, AudioBus bus = AudioBus.Sfx, float volume = 1f)
        {
            if (clip == null || _pool == null) return null;
            AudioSource source = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            source.Stop();
            source.clip = clip;
            source.loop = false;
            source.volume = Mathf.Clamp01(volume) * BusVolume(bus);
            source.Play();
            return source;
        }

        public static void Vibrate()
        {
            if (SettingsService.Instance == null) return;
            if (!SettingsService.Instance.Data.controls.vibration) return;
            Handheld.Vibrate();
        }
    }
}
