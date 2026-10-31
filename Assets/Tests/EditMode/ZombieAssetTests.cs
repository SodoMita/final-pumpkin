using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Foundation.Tests
{
    /// <summary>
    /// Checks the Anny zombie assets that ship with the project: an FBX per
    /// variant imported as a Legacy rig, plus the AI-generated albedo map that
    /// goes with it. Runs in edit mode so a broken bake fails fast.
    /// </summary>
    public class ZombieAssetTests
    {
        static string AssetPath(ZombieVariant variant)
        {
            return "Assets/Resources/Zombies/zombie_" + variant.ToString().ToLowerInvariant();
        }

        [Test]
        public void EveryVariantShipsAfbx()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                string path = AssetPath(variant) + ".fbx";
                Assert.IsTrue(File.Exists(path), $"Missing zombie model: {path}");
                Assert.Greater(new FileInfo(path).Length, 256 * 1024, $"Suspiciously small model: {path}");
            }
        }

        [Test]
        public void EveryVariantShipsAnAiAlbedoMap()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                string path = AssetPath(variant) + "_albedo.png";
                Assert.IsTrue(File.Exists(path), $"Missing albedo: {path}");

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(texture, $"Albedo did not import as a texture: {path}");
                Assert.GreaterOrEqual(texture.width, 512, "Albedo is too small to read as detail");
                Assert.AreEqual(texture.width, texture.height, "Albedo should be square (the bake is square)");
            }
        }

        [Test]
        public void ModelsImportAsLegacyRigsWithThreeClips()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                string path = AssetPath(variant) + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(importer, $"No ModelImporter for {path}");
                Assert.AreEqual(ModelImporterAnimationType.Legacy, importer.animationType,
                                $"{path} must import as a Legacy rig or the clips need an AnimatorController");
                Assert.IsTrue(importer.importAnimation, $"{path} must import animation");

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                int clips = 0;
                int skinned = 0;
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) clips++;
                    if (asset is GameObject go && go.GetComponentInChildren<SkinnedMeshRenderer>() != null) skinned++;
                }

                Assert.AreEqual(3, clips, $"{path} should contain Shamble, Idle and Lunge");
                Assert.GreaterOrEqual(skinned, 1, $"{path} has no skinned mesh");
            }
        }

        [Test]
        public void SkeletonIsTheAnnySkeleton()
        {
            string path = AssetPath(ZombieVariant.Shambler) + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, "Shambler did not import as a prefab");

            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(skin);
            Assert.GreaterOrEqual(skin.bones.Length, 100, "Expected Anny's 104-bone rig");
            Assert.Greater(skin.sharedMesh.vertexCount, 10000, "Unexpectedly coarse mesh");
        }
    }
}
