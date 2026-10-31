using UnityEditor;
using UnityEngine;

namespace Foundation.EditorTools
{
    /// <summary>
    /// The zombie and prop models are imported as Legacy-rig assets: that gives
    /// every baked clip as a legacy AnimationClip on one Animation component,
    /// which is what the runtime components drive (a Mecanim setup would need an
    /// AnimatorController asset per model).
    ///
    /// Without this pass Unity picks a Generic rig and the clips can only be
    /// played by an Animator, so the models would stand frozen.
    /// </summary>
    public sealed class ZombieModelImporter : AssetPostprocessor
    {
        static readonly string[] ModelFolders = { "Resources/Zombies/", "Resources/Props/" };

        static readonly string[] PaletteTextures = { "Resources/Props/" };

        void OnPreprocessModel()
        {
            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;
            if (!InFolder(ModelFolders)) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;

            // Deliberately no SearchAndRemapMaterials here: calling it from
            // OnPreprocessModel makes Unity reimport the model forever, which
            // hangs the batch-mode test runner. The runtime components assign
            // their own materials anyway, so the importer's default is fine.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        /// The pumpkin's colours live in a 5x5 palette image: every pixel is a
        /// flat colour and the mesh UVs point at individual cells, so any kind of
        /// filtering or compression blends neighbouring colours together.
        void OnPreprocessTexture()
        {
            if (!InFolder(PaletteTextures)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 32;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
        }

        bool InFolder(string[] folders)
        {
            foreach (string folder in folders)
                if (assetPath.IndexOf(folder, System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
