using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// The four zombies generated from the naver/anny parametric body model by
    /// <c>tools/anny_zombies/anny_zombie_export.py</c>, converted to FBX by
    /// <c>tools/anny_zombies/bake_fbx.py</c> and painted by
    /// <c>tools/anny_zombies/texture_bake.py</c>.
    /// </summary>
    public enum ZombieVariant
    {
        Shambler,
        Bloater,
        Fresh,
        Small,
    }

    /// <summary>One spawned zombie plus its animation plumbing.</summary>
    public sealed class Zombie
    {
        public ZombieVariant Variant { get; internal set; }
        public GameObject Root { get; internal set; }
        public SkinnedMeshRenderer Skin { get; internal set; }
        public Animation Animation { get; internal set; }
        public ZombieAnimationDriver Driver { get; internal set; }
        public AnimationClip[] Clips { get; internal set; }

        public float Height
        {
            get { return Skin != null ? Skin.bounds.size.y : 0f; }
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null;
            Skin = null;
            Animation = null;
            Driver = null;
            Clips = null;
        }
    }

    /// <summary>
    /// Spawns the Anny zombies through Unity's own model importer.
    ///
    /// The FBX files live in Resources/Zombies and are imported by
    /// Assets/Editor/ZombieModelImporter.cs with a Legacy rig, so each zombie
    /// arrives with a SkinnedMeshRenderer, the 104-bone Anny skeleton and the
    /// three baked clips already on an Animation component - no runtime glTF
    /// package, no shader-side curve sampling, no conversion step in the build.
    /// The AI-generated albedo map sits next to each FBX and is applied here.
    /// </summary>
    public static class ZombieFactory
    {
        public const string ResourceFolder = "Zombies";
        public const string ShambleClipName = "Shamble";
        public const string IdleClipName = "Idle";
        public const string LungeClipName = "Lunge";

        /// <summary>Tint used when a variant's albedo map is missing.</summary>
        public static readonly Color FallbackColour = new Color(0.42f, 0.47f, 0.36f);

        public static ZombieVariant[] AllVariants =>
            (ZombieVariant[])System.Enum.GetValues(typeof(ZombieVariant));

        public static string ModelPath(ZombieVariant variant)
        {
            return ResourceFolder + "/zombie_" + variant.ToString().ToLowerInvariant();
        }

        public static string AlbedoPath(ZombieVariant variant)
        {
            return ModelPath(variant) + "_albedo";
        }

        /// <summary>Instantiates one zombie under <paramref name="parent"/>.</summary>
        public static Zombie Create(ZombieVariant variant, Transform parent,
                                    Vector3 position = default, float yawDegrees = 0f)
        {
            var prefab = Resources.Load<GameObject>(ModelPath(variant));
            if (prefab == null)
            {
                Debug.LogError($"ZombieFactory: no model at Resources/{ModelPath(variant)}");
                return null;
            }

            GameObject root = Object.Instantiate(prefab);
            root.name = "Zombie_" + variant;
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            var zombie = new Zombie
            {
                Variant = variant,
                Root = root,
                Skin = root.GetComponentInChildren<SkinnedMeshRenderer>(),
                Animation = root.GetComponentInChildren<Animation>(),
            };

            if (zombie.Skin == null)
            {
                Debug.LogError($"ZombieFactory: {variant} has no SkinnedMeshRenderer");
                Object.Destroy(root);
                return null;
            }

            ApplyAlbedo(zombie);
            zombie.Clips = ConfigureAnimation(zombie.Animation);

            if (zombie.Animation != null)
            {
                zombie.Driver = root.AddComponent<ZombieAnimationDriver>();
                zombie.Driver.Bind(zombie.Animation);
            }

            return zombie;
        }

        /// <summary>The AI albedo map, applied to every material slot of the mesh.</summary>
        static void ApplyAlbedo(Zombie zombie)
        {
            var albedo = Resources.Load<Texture2D>(AlbedoPath(zombie.Variant));
            if (albedo == null)
            {
                Debug.LogWarning($"ZombieFactory: no albedo for {zombie.Variant}, using the flat colour");
                MaterialFactory.Apply(zombie.Skin, FallbackColour, 0.3f);
                return;
            }

            Material material = MaterialFactory.Textured(albedo, Color.white, 0.3f);
            if (material == null) return;

            var materials = new Material[zombie.Skin.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            zombie.Skin.sharedMaterials = materials;
        }

        /// <summary>
        /// Collects the clips the importer produced and sets their wrap modes:
        /// Lunge is a one-shot, the other two loop.
        /// </summary>
        static AnimationClip[] ConfigureAnimation(Animation animation)
        {
            if (animation == null) return new AnimationClip[0];

            var clips = new List<AnimationClip>();
            foreach (AnimationState state in animation)
            {
                AnimationClip clip = state.clip;
                if (clip == null) continue;
                clip.wrapMode = clip.name == LungeClipName ? WrapMode.Once : WrapMode.Loop;
                clips.Add(clip);
            }

            animation.playAutomatically = false;
            animation.wrapMode = WrapMode.Loop;

            AnimationClip first = FindClip(clips, ShambleClipName);
            if (first != null)
            {
                animation.clip = first;
                animation.Play(first.name);
            }
            return clips.ToArray();
        }

        static AnimationClip FindClip(List<AnimationClip> clips, string name)
        {
            foreach (AnimationClip clip in clips)
                if (clip != null && clip.name == name) return clip;
            return clips.Count > 0 ? clips[0] : null;
        }

        /// <summary>True when every variant has both its model and its albedo map.</summary>
        public static bool AllAssetsPresent()
        {
            foreach (ZombieVariant variant in AllVariants)
            {
                if (Resources.Load<GameObject>(ModelPath(variant)) == null) return false;
                if (Resources.Load<Texture2D>(AlbedoPath(variant)) == null) return false;
            }
            return true;
        }
    }
}
