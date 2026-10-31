using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;
using UnityEngine.Networking;

namespace Foundation
{
    /// <summary>
    /// The four zombies generated from the naver/anny parametric body model by
    /// <c>tools/anny_zombies/anny_zombie_export.py</c>.
    /// </summary>
    public enum ZombieVariant
    {
        Shambler,
        Bloater,
        Fresh,
        Small,
    }

    /// <summary>One loaded zombie instance plus its animation plumbing.</summary>
    public sealed class Zombie : IDisposable
    {
        public ZombieVariant Variant { get; internal set; }
        public GameObject Root { get; internal set; }
        public Transform Scene { get; internal set; }
        public Animation Animation { get; internal set; }
        public ZombieAnimationDriver Driver { get; internal set; }
        public AnimationClip[] Clips { get; internal set; }
        internal GltfImport Import { get; set; }

        public float Height
        {
            get
            {
                Renderer renderer = Root != null ? Root.GetComponentInChildren<Renderer>() : null;
                return renderer != null ? renderer.bounds.size.y : 0f;
            }
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            Animation = null;
            Driver = null;
            Clips = null;

            if (Import != null)
            {
                Import.Dispose();
                Import = null;
            }
        }
    }

    /// <summary>
    /// Loads the Anny-authored glTF-binary zombies at runtime.
    ///
    /// The meshes ship in StreamingAssets instead of Assets/Models because the
    /// project has no Editor importer for .glb: anything else under Assets/
    /// would be ignored by Unity. glTFast turns the binaries into
    /// SkinnedMeshRenderers with a 104-bone skeleton and the baked
    /// Shamble/Idle/Lunge clips; the import is pinned to the legacy animation
    /// method so a plain <see cref="Animation"/> component can play them
    /// (Mecanim controllers cannot be built in a player build).
    /// </summary>
    public static class ZombieFactory
    {
        public const string FolderName = "Zombies";
        public const string ShambleClipName = "Shamble";
        public const string IdleClipName = "Idle";
        public const string LungeClipName = "Lunge";

        public static ZombieVariant[] AllVariants =>
            (ZombieVariant[])Enum.GetValues(typeof(ZombieVariant));

        public static string FileName(ZombieVariant variant)
        {
            return "zombie_" + variant.ToString().ToLowerInvariant() + ".glb";
        }

        public static string StreamingFolder
        {
            get { return Path.Combine(Application.streamingAssetsPath, FolderName); }
        }

        public static string StreamingPath(ZombieVariant variant)
        {
            return Path.Combine(StreamingFolder, FileName(variant));
        }

        /// <summary>Loads and instantiates one zombie under <paramref name="parent"/>.</summary>
        public static async Task<Zombie> CreateAsync(
            ZombieVariant variant,
            Transform parent,
            Vector3 position = default,
            float yawDegrees = 0f)
        {
            string path = StreamingPath(variant);
            byte[] bytes = await ReadBytesAsync(path);
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogError($"ZombieFactory: could not read {path}");
                return null;
            }

            var root = new GameObject("Zombie_" + variant);
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);

            var import = new GltfImport();
            var importSettings = new ImportSettings { AnimationMethod = AnimationMethod.Legacy };
            bool loaded = await import.LoadGltfBinary(bytes, new Uri(path), importSettings);
            if (!loaded)
            {
                Debug.LogError($"ZombieFactory: glTFast failed to load {path}");
                UnityEngine.Object.Destroy(root);
                import.Dispose();
                return null;
            }

            var settings = new InstantiationSettings
            {
                SceneObjectCreation = SceneObjectCreation.Always,
                SkinUpdateWhenOffscreen = true,
            };
            var instantiator = new GameObjectInstantiator(import, root.transform, null, settings);
            bool instantiated = await import.InstantiateMainSceneAsync(instantiator);
            if (!instantiated)
            {
                Debug.LogError($"ZombieFactory: glTFast failed to instantiate {path}");
                UnityEngine.Object.Destroy(root);
                import.Dispose();
                return null;
            }

            Transform scene = instantiator.SceneTransform;
            AnimationClip[] clips = import.GetAnimationClips();
            Animation animation = ConfigureAnimation(instantiator, clips);

            var zombie = new Zombie
            {
                Variant = variant,
                Root = root,
                Scene = scene,
                Animation = animation,
                Clips = clips,
                Import = import,
            };

            if (animation != null) zombie.Driver = scene.gameObject.AddComponent<ZombieAnimationDriver>();
            if (zombie.Driver != null) zombie.Driver.Bind(animation);

            return zombie;
        }

        /// <summary>
        /// glTFast already attached an Animation component and registered every
        /// clip on it (all clips come back as WrapMode.Loop). All that is left is
        /// to make Lunge a one-shot and start on the walk cycle.
        /// </summary>
        static Animation ConfigureAnimation(GameObjectInstantiator instantiator, AnimationClip[] clips)
        {
            if (instantiator == null || instantiator.SceneInstance == null) return null;

            Animation animation = instantiator.SceneInstance.LegacyAnimation;
            if (animation == null) return null;

            foreach (AnimationClip clip in clips)
            {
                if (clip == null) continue;
                clip.wrapMode = clip.name == LungeClipName ? WrapMode.Once : WrapMode.Loop;
            }

            animation.playAutomatically = false;
            animation.wrapMode = WrapMode.Loop;

            AnimationClip first = FindClip(clips, ShambleClipName) ?? (clips.Length > 0 ? clips[0] : null);
            if (first != null)
            {
                animation.clip = first;
                animation.Play(first.name);
            }
            return animation;
        }

        static AnimationClip FindClip(AnimationClip[] clips, string name)
        {
            foreach (AnimationClip clip in clips)
                if (clip != null && clip.name == name) return clip;
            return null;
        }

        /// <summary>
        /// UnityWebRequest instead of File.ReadAllBytes so the same call works on
        /// Android, where StreamingAssets lives inside the APK.
        /// </summary>
        static async Task<byte[]> ReadBytesAsync(string path)
        {
            string uri = path.Contains("://") ? path : "file://" + path;
            using (UnityWebRequest request = UnityWebRequest.Get(uri))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                await request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"ZombieFactory: {uri} -> {request.error}");
                    return null;
                }
                return request.downloadHandler.data;
            }
        }
    }
}
