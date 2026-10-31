using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Foundation.Tests
{
    /// <summary>
    /// Spawns the real Anny zombies in play mode: FBX model, imported skeleton,
    /// AI albedo, legacy clips playing and actually moving bones.
    /// </summary>
    public class ZombieAnimationTests
    {
        readonly List<Zombie> _spawned = new List<Zombie>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Zombie zombie in _spawned) zombie.Dispose();
            _spawned.Clear();
            yield return null;
        }

        Zombie Spawn(ZombieVariant variant)
        {
            Zombie zombie = ZombieFactory.Create(variant, null);
            if (zombie != null) _spawned.Add(zombie);
            return zombie;
        }

        [UnityTest]
        public IEnumerator ShamblerSpawnsWithSkinClipsAndTexture()
        {
            Zombie zombie = Spawn(ZombieVariant.Shambler);
            yield return null;

            Assert.IsNotNull(zombie, "Shambler failed to spawn");
            Assert.IsNotNull(zombie.Skin, "No SkinnedMeshRenderer");
            Assert.IsNotNull(zombie.Animation, "No legacy Animation component");
            Assert.IsNotNull(zombie.Driver, "No animation driver");
            Assert.AreEqual(3, zombie.Clips.Length, "Expected Shamble, Idle and Lunge");
            Assert.GreaterOrEqual(zombie.Skin.bones.Length, 100, "Unexpectedly small skeleton");
            Assert.IsTrue(zombie.Height > 1.4f, $"Shambler is {zombie.Height:0.00} m tall");

            Material material = zombie.Skin.sharedMaterial;
            Assert.IsNotNull(material, "No material on the zombie");
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap")
                                                             : material.mainTexture;
            Assert.IsNotNull(texture, "The AI albedo map was not applied");
        }

        [UnityTest]
        public IEnumerator VariantsHaveDistinctHeights()
        {
            var heights = new Dictionary<ZombieVariant, float>();
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                Zombie zombie = Spawn(variant);
                Assert.IsNotNull(zombie, $"{variant} failed to spawn");
                heights[variant] = zombie.Height;
                yield return null;
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
            Zombie zombie = Spawn(ZombieVariant.Bloater);
            yield return null;
            Assert.IsNotNull(zombie);
            Assert.IsTrue(zombie.Animation.isPlaying, "Animation is not playing");
            Assert.AreEqual(ZombieFactory.ShambleClipName, zombie.Driver.CurrentClip);

            Transform[] bones = zombie.Skin.bones;
            var before = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) before[i] = bones[i].localRotation;

            float wait = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < wait) yield return null;

            float moved = 0f;
            for (int i = 0; i < bones.Length; i++)
                moved = Mathf.Max(moved, Quaternion.Angle(before[i], bones[i].localRotation));

            Assert.Greater(moved, 0.5f, "No bone rotated, so the clips are not driving the rig");
        }

        [UnityTest]
        public IEnumerator DriverSwitchesBetweenClips()
        {
            Zombie zombie = Spawn(ZombieVariant.Fresh);
            yield return null;
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
        public IEnumerator PumpkinSpawnsTexturedAndAnimated()
        {
            var host = new GameObject("PumpkinHost");
            PumpkinProp pumpkin = PumpkinProp.Create(host.transform, Vector3.zero);
            yield return null;

            Assert.IsNotNull(pumpkin, "The pumpkin prop failed to spawn");
            Assert.IsNotNull(pumpkin.Animation, "The pumpkin has no Animation component");
            Assert.Greater(pumpkin.Clips.Length, 0, "The pumpkin has no clips");
            Assert.IsTrue(pumpkin.IsPlaying, "The pumpkin is not animating");

            var renderers = host.GetComponentsInChildren<Renderer>();
            Assert.GreaterOrEqual(renderers.Length, 1, "The pumpkin has no renderers");
            Material material = renderers[0].sharedMaterial;
            Assert.IsNotNull(material, "The pumpkin has no material");
            Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap")
                                                             : material.mainTexture;
            Assert.IsNotNull(texture, "The palette texture was not applied");

            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnerBuildsTheWholeHorde()
        {
            var host = new GameObject("ZombieTestHost");
            var spawner = host.AddComponent<ZombieSpawner>();
            yield return null;

            Assert.IsTrue(spawner.LoadComplete, "Spawner never finished");
            Assert.IsFalse(spawner.LoadFailed, "At least one zombie failed to spawn");
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
