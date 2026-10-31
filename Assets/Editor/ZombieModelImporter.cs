using UnityEditor;
using UnityEngine;

namespace Foundation.EditorTools
{
    /// <summary>
    /// The zombie FBX files are imported as Legacy-rig models: that gives every
    /// baked clip (Shamble, Idle, Lunge) as a legacy AnimationClip on one
    /// Animation component, which is what <see cref="Foundation.ZombieAnimationDriver"/>
    /// drives. Mecanim would need an AnimatorController asset per zombie.
    ///
    /// Without this the default importer chooses a Generic rig and the clips can
    /// only be played by an Animator, so the zombies would stand frozen.
    /// </summary>
    public sealed class ZombieModelImporter : AssetPostprocessor
    {
        const string Folder = "Resources/Zombies/";

        void OnPreprocessModel()
        {
            if (assetPath.IndexOf(Folder, System.StringComparison.Ordinal) < 0) return;
            if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return;

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
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName,
                                            ModelImporterMaterialSearch.Everywhere);
        }
    }
}
