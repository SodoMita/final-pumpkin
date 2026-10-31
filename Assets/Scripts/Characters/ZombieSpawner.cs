using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Places the four Anny zombies in the level and keeps their animation
    /// drivers pointed at the player. Loading is asynchronous (glTFast parses the
    /// binaries over several frames), so the level appears first and the zombies
    /// drop in a moment later.
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

        /// <summary>True once every spawn attempt has finished, successfully or not.</summary>
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

        /// <summary>Fire-and-forget entry point used by the level builder.</summary>
        public void SpawnDefaults()
        {
            if (LoadComplete || _zombies.Count > 0) return;
            _ = SpawnAllAsync(DefaultLayout);
        }

        public async Task SpawnAllAsync(IEnumerable<SpawnPoint> layout)
        {
            LoadComplete = false;
            LoadFailed = false;

            var tasks = new List<Task<Zombie>>();
            foreach (SpawnPoint point in layout)
            {
                tasks.Add(ZombieFactory.CreateAsync(point.Variant, transform, point.Position, point.Yaw));
            }

            while (tasks.Count > 0)
            {
                Task<Zombie> finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);

                Zombie zombie = finished.Result;
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
