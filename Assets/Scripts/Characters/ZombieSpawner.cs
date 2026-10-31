using System.Collections.Generic;
using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Places the four Anny zombies in the level and keeps their animation
    /// drivers pointed at the player. The FBX models are imported by Unity, so
    /// spawning is a plain Resources load and Instantiate.
    /// </summary>
    public sealed class ZombieSpawner : MonoBehaviour
    {
        /// <summary>A variant, where it stands and which way it faces.</summary>
        public readonly struct SpawnPoint
        {
            public SpawnPoint(ZombieVariant variant, Vector3 position, float yaw)
            {
                Variant = variant;
                Position = position;
                Yaw = yaw;
            }

            public ZombieVariant Variant { get; }
            public Vector3 Position { get; }
            public float Yaw { get; }
        }

        /// <summary>Variant, position and facing for each zombie in the test level.</summary>
        public static readonly SpawnPoint[] DefaultLayout =
        {
            new SpawnPoint(ZombieVariant.Shambler, new Vector3(-3.2f, 0f, -3.2f), 25f),
            new SpawnPoint(ZombieVariant.Bloater, new Vector3(4.2f, 0f, -6.5f), -35f),
            new SpawnPoint(ZombieVariant.Fresh, new Vector3(-6.5f, 0f, 0.5f), 90f),
            new SpawnPoint(ZombieVariant.Small, new Vector3(1.6f, 0f, -4.2f), 160f),
        };

        readonly List<Zombie> _zombies = new List<Zombie>();

        /// <summary>Set by LevelBuilder; zombies turn to face it while it is near.</summary>
        public Transform Target { get; set; }

        /// <summary>Number of zombies that finished loading.</summary>
        public int LoadedCount
        {
            get { return _zombies.Count; }
        }

        /// <summary>True once every spawn attempt has run, successfully or not.</summary>
        public bool LoadComplete { get; private set; }

        /// <summary>True if at least one zombie failed to load.</summary>
        public bool LoadFailed { get; private set; }

        public IReadOnlyList<Zombie> Zombies
        {
            get { return _zombies; }
        }

        void Start()
        {
            SpawnDefaults();
        }

        public void SpawnDefaults()
        {
            if (LoadComplete || _zombies.Count > 0) return;
            SpawnAll(DefaultLayout);
        }

        public void SpawnAll(IEnumerable<SpawnPoint> layout)
        {
            LoadFailed = false;

            foreach (SpawnPoint point in layout)
            {
                Zombie zombie = ZombieFactory.Create(point.Variant, transform, point.Position, point.Yaw);
                if (zombie == null)
                {
                    LoadFailed = true;
                    continue;
                }

                if (zombie.Driver != null) zombie.Driver.Target = Target;
                _zombies.Add(zombie);
            }

            LoadComplete = true;
        }

        /// <summary>Points every loaded driver at a new target (e.g. after a respawn).</summary>
        public void Retarget(Transform target)
        {
            Target = target;
            foreach (Zombie zombie in _zombies)
                if (zombie.Driver != null) zombie.Driver.Target = target;
        }

        void OnDestroy()
        {
            foreach (Zombie zombie in _zombies) zombie.Dispose();
            _zombies.Clear();
        }
    }
}
