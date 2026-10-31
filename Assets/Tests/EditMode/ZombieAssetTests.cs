using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests
{
    /// <summary>
    /// Cheap structural checks on the baked Anny zombies. These run in edit mode
    /// (no glTFast, no player loop) so a broken exporter is caught before the
    /// heavier play mode tests try to load anything.
    /// </summary>
    public class ZombieAssetTests
    {
        static string PathFor(ZombieVariant variant)
        {
            return Path.Combine(Application.streamingAssetsPath, ZombieFactory.FolderName,
                                ZombieFactory.FileName(variant));
        }

        [Test]
        public void EveryVariantShipsAGlb()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                string path = PathFor(variant);
                Assert.IsTrue(File.Exists(path), $"Missing zombie asset: {path}");
                Assert.Greater(new FileInfo(path).Length, 64 * 1024, $"Suspiciously small asset: {path}");
            }
        }

        [Test]
        public void AssetsAreGltfBinaryVersion2()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                string path = PathFor(variant);
                byte[] header = new byte[12];
                long length;
                using (FileStream stream = File.OpenRead(path))
                {
                    Assert.GreaterOrEqual(stream.Length, 20, "GLB too short");
                    length = stream.Length;
                    stream.Read(header, 0, header.Length);
                }

                Assert.AreEqual("glTF", Encoding.ASCII.GetString(header, 0, 4), "Bad GLB magic");
                Assert.AreEqual(2, System.BitConverter.ToInt32(header, 4), "Unexpected GLB version");
                Assert.AreEqual(length, System.BitConverter.ToInt32(header, 8),
                                "GLB length header does not match the file");
            }
        }

        [Test]
        public void AssetsCarrySkinnedMeshAndThreeClips()
        {
            foreach (ZombieVariant variant in ZombieFactory.AllVariants)
            {
                byte[] bytes = File.ReadAllBytes(PathFor(variant));
                string json = ExtractJsonChunk(bytes, 12);

                StringAssert.Contains("\"skins\"", json, $"{variant} has no skin");
                StringAssert.Contains("JOINTS_0", json, $"{variant} has no joint weights");
                StringAssert.Contains("\"" + ZombieFactory.ShambleClipName + "\"", json,
                                      $"{variant} has no Shamble clip");
                StringAssert.Contains("\"" + ZombieFactory.IdleClipName + "\"", json,
                                      $"{variant} has no Idle clip");
                StringAssert.Contains("\"" + ZombieFactory.LungeClipName + "\"", json,
                                      $"{variant} has no Lunge clip");
            }
        }

        /// The GLB layout is: 12-byte header, then chunks of (length, type, payload)
        /// starting at offset 12. The first chunk is always the JSON document.
        static string ExtractJsonChunk(byte[] bytes, int offset)
        {
            int chunkLength = System.BitConverter.ToInt32(bytes, offset);
            int chunkType = System.BitConverter.ToInt32(bytes, offset + 4);
            Assert.AreEqual(0x4E4F534A, chunkType, "First GLB chunk is not JSON");
            return Encoding.UTF8.GetString(bytes, offset + 8, chunkLength);
        }
    }
}
