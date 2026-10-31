using System.Collections.Generic;
using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// The pumpkin prop that came with the project: one FBX holding three parts
    /// (the two halves and the stem/seed cluster) and nine short transform clips
    /// that split it apart. There is no skeleton, so the clips drive the part
    /// transforms directly, which is exactly what a legacy Animation does.
    /// </summary>
    public sealed class PumpkinProp : MonoBehaviour
    {
        public const string ResourcePath = "Props/animated_pumpkin";
        public const string PalettePath = "Props/pumpkin_colors";

        /// <summary>The pumpkin is modelled about 2.6 m wide; this brings it to ~1 m.</summary>
        public const float Scale = 0.4f;

        /// <summary>Height of the split halves in metres, used to sit it on the floor.</summary>
        public const float GroundOffset = 0.38f;

        public Animation Animation { get; private set; }
        public AnimationClip[] Clips { get; private set; }
        public string CurrentClip { get; private set; }

        /// <summary>
        /// Instantiates the pumpkin, textures it from the palette image and
        /// starts its first clip. Returns null (with a warning) when the FBX is
        /// missing, so the level still builds.
        /// </summary>
        public static PumpkinProp Create(Transform parent, Vector3 position, float yawDegrees = 0f)
        {
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"PumpkinProp: no model at Resources/{ResourcePath}");
                return null;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = "Pumpkin";
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            instance.transform.localScale = Vector3.one * Scale;

            var prop = instance.AddComponent<PumpkinProp>();
            prop.ApplyPalette();
            prop.CollectClips();
            prop.PlayDefault();
            return prop;
        }

        void ApplyPalette()
        {
            var palette = Resources.Load<Texture2D>(PalettePath);
            if (palette == null)
            {
                Debug.LogWarning("PumpkinProp: palette image missing, the pumpkin will be flat coloured");
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
                    MaterialFactory.Apply(renderer, PumpkinColour, 0.15f);
                return;
            }

            Material material = MaterialFactory.Textured(palette, Color.white, 0.15f);
            if (material == null) return;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        void CollectClips()
        {
            Animation = GetComponentInChildren<Animation>();
            if (Animation == null)
            {
                Clips = new AnimationClip[0];
                return;
            }

            var clips = new List<AnimationClip>();
            foreach (AnimationState state in Animation)
            {
                if (state.clip == null) continue;
                state.clip.wrapMode = WrapMode.Once;
                clips.Add(state.clip);
            }

            Animation.playAutomatically = false;
            Clips = clips.ToArray();
        }

        /// <summary>
        /// The clips are three copies of the same slice animation (Blender wrote
        /// one action per part), so playing the first one is enough for the
        /// background prop. Falls back silently when the FBX shipped no clips.
        /// </summary>
        public void PlayDefault()
        {
            if (Clips == null || Clips.Length == 0 || Animation == null) return;
            Play(Clips[0].name);
        }

        public void Play(string clipName)
        {
            if (Animation == null || string.IsNullOrEmpty(clipName)) return;
            if (Animation.GetClip(clipName) == null) return;

            Animation.Stop();
            Animation.clip = Animation.GetClip(clipName);
            Animation.wrapMode = Animation.clip.wrapMode;
            Animation.Play(clipName);
            CurrentClip = clipName;
        }

        /// <summary>Restarts the slice, which is what an attack or a hit would trigger.</summary>
        public void PlaySlice()
        {
            if (Clips == null || Clips.Length == 0) return;
            Animation.Stop();
            Play(Clips[0].name);
        }

        public bool IsPlaying => Animation != null && Animation.isPlaying;

        public static readonly Color PumpkinColour = new Color(0.80f, 0.42f, 0.10f);
    }
}
