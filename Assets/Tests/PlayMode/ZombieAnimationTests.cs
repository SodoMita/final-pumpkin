using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Foundation.Tests
{
    /// <summary>
    /// Loads the real Anny zombies through glTFast in play mode. This is the test
    /// that proves the runtime pipeline works end to end: binary in
    /// StreamingAssets -> skinned mesh in the scene -> legacy clips playing and
    /// actually moving bones.
    /// </summary>
    public class ZombieAnimationTests
    {
        readonly List<Zombie> _loaded = new List<Zombie>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Zombie zombie in _loaded) zombie.Dispose();
            _loaded.Clear();
            yield return null;
        }

        Zombie Track(Zombie zombie)
        {
            if (zombie != null) _loaded.Add(zombie);
            return zombie;
        }

        static IEnumerator Await(Task<Zombie> task)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Zombie load timed out");
        }

        [UnityTest]
        public IEnumerator ShamblerLoadsWithSkinAndClips()
        {
            Task<Zombie> task = ZombieFactory.CreateAsync(ZombieVariant.Shambler, null);
            yield return Await(task);
            Zombie zombie = Track(task.Result);

            Assert.IsNotNull(zombie, "Shambler failed to load");
            Assert.IsNotNull(zombie.Root);
            Assert.IsNotNull(zombie.Animation, "No legacy Animation component");
            Assert.IsNotNull(zombie.Driver, "No animation driver");
            Assert.AreEqual(3, zombie.Clips.Length, "Expected Shamble, Idle and Lunge");

            var skinned = zombie.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(skinned, "No SkinnedMeshRenderer");
            Assert.GreaterOrEqual(skinned.bones.Length, 100, "Unexpectedly small skeleton");
            Assert.Greater(skinned.sharedMesh.vertexCount, 1000);
            Assert.IsTrue(zombie.Height > 1.4f, $"Shambler is {zombie.Height:0.00} m tall");
        }

        [UnityTest]
        public IEnumerator AllVariantsHaveDistinctHeights()
        {
            var heights = new Dictionary<ZombieVariant, float>();
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                Task<Zombie> task = ZombieFactory.CreateAsync(
                    variant, null, new Vector3(100f * heights.Count, 0f, 100f));
                yield return Await(task);
                Zombie zombie = Track(task.Result);
                Assert.IsNotNull(zombie, $"{variant} failed to load");
                heights[variant] = zombie.Height;
            }

            Assert.AreEqual(4, heights.Count);
            Assert.Greater(heights[ZombieVariant.Shambler], heights[ZombieVariant.Bloater] - 0.2f);
            Assert.Greater(heights[ZombieVariant.Bloater], heights[ZombieVariant.Fresh] + 0.3f);
            Assert.Greater(heights[ZombieVariant.Fresh], heights[ZombieVariant.Small] + 0.1f);
            foreach (KeyValuePair<ZombieVariant, float> pair in heights)
                Assert.Greater(pair.Value, 0.5f, $"{pair.Key} is under half a metre tall");
        }

        [UnityTest]
        public IEnumerator PlaybackMovesTheSkeleton()
        {
            Task<Zombie> task = ZombieFactory.CreateAsync(ZombieVariant.Bloater, null);
            yield return Await(task);
            Zombie zombie = Track(task.Result);
            Assert.IsNotNull(zombie);

            Assert.IsTrue(zombie.Animation.isPlaying, "Animation is not playing");
            Assert.AreEqual(ZombieFactory.ShambleClipName, zombie.Driver.CurrentClip);

            Transform bone = zombie.Root.GetComponentInChildren<SkinnedMeshRenderer>().bones[20];
            Quaternion before = bone.localRotation;

            // Let a second of the walk cycle through.
            float wait = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < wait) yield return null;

            Assert.Greater(Quaternion.Angle(before, bone.localRotation), 0.5f,
                "A bone did not move, so the clip is not actually driving the rig");
        }

        [UnityTest]
        public IEnumerator DriverSwitchesBetweenClips()
        {
            Task<Zombie> task = ZombieFactory.CreateAsync(ZombieVariant.Fresh, null);
            yield return Await(task);
            Zombie zombie = Track(task.Result);
            Assert.IsNotNull(zombie);

            zombie.Driver.ForceState(ZombieAnimationDriver.State.Idle, 5f);
            yield return null;
            Assert.AreEqual(ZombieFactory.IdleClipName, zombie.Driver.CurrentClip);

            zombie.Driver.ForceState(ZombieAnimationDriver.State.Lunge, 2f);
            yield return null;
            Assert.AreEqual(ZombieFactory.LungeClipName, zombie.Driver.CurrentClip);

            zombie.Driver.ForceState(ZombieAnimationDriver.State.Shamble, 5f);
            yield return null;
            Assert.AreEqual(ZombieFactory.ShambleClipName, zombie.Driver.CurrentClip);
        }

        [UnityTest]
        public IEnumerator SpawnerBuildsTheWholeHorde()
        {
            var host = new GameObject("ZombieTestHost");
            var spawner = host.AddComponent<ZombieSpawner>();

            float deadline = Time.realtimeSinceStartup + 90f;
            while (!spawner.LoadComplete && Time.realtimeSinceStartup < deadline) yield return null;

            Assert.IsTrue(spawner.LoadComplete, "Spawner never finished loading");
            Assert.IsFalse(spawner.LoadFailed, "At least one zombie failed to load");
            Assert.AreEqual(ZombieSpawner.DefaultLayout.Length, spawner.LoadedCount);

            foreach (Zombie zombie in spawner.Zombies)
            {
                Assert.IsNotNull(zombie.Root);
                Assert.IsNotNull(zombie.Driver);
                Assert.IsTrue(zombie.Animation.isPlaying);
            }

            Object.Destroy(host);
            yield return null;
        }
    }
}
