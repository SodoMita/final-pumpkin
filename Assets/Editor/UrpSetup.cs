using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Foundation.EditorTools
{
    /// <summary>
    /// Assigns a Universal Render Pipeline asset the first time the project is
    /// opened, so nobody has to click through Project Settings -> Graphics.
    /// Safe to re-run; it does nothing once a pipeline is already assigned.
    /// </summary>
    [InitializeOnLoad]
    public static class UrpSetup
    {
        const string SettingsFolder = "Assets/Settings";
        const string RendererPath = SettingsFolder + "/UniversalRenderer.asset";
        const string PipelinePath = SettingsFolder + "/UniversalRP.asset";
        const string ResourcesFolder = "Assets/Resources";
        const string DefaultMaterialPath = ResourcesFolder + "/FoundationDefaultMaterial.mat";

        static UrpSetup()
        {
            EditorApplication.delayCall += () => EnsurePipeline();
        }

        [MenuItem("Tools/Foundation/Set Up URP")]
        public static void SetupFromMenu()
        {
            EnsurePipeline(true);
        }

        static void EnsurePipeline(bool force = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!force && GraphicsSettings.defaultRenderPipeline != null) return;

            try
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "Settings");
                }

                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (rendererData == null)
                {
                    rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(rendererData, RendererPath);
                }

                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (pipeline == null)
                {
                    pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }

                AssetDatabase.SaveAssets();
                GraphicsSettings.defaultRenderPipeline = pipeline;

                int levels = QualitySettings.names.Length;
                for (int level = 0; level < levels; level++)
                {
                    QualitySettings.SetQualityLevel(level, false);
                    QualitySettings.renderPipeline = pipeline;
                }
                QualitySettings.SetQualityLevel(levels - 1, false);

                EnsureDefaultMaterial(pipeline);
                AssetDatabase.SaveAssets();
                Debug.Log("[Foundation] URP asset created and assigned: " + PipelinePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Foundation] Could not set up URP automatically (" + e.Message +
                                 "). Assign Assets/Settings/UniversalRP.asset in Project Settings > Graphics. " +
                                 "The game also runs on the Built-in pipeline.");
            }
        }

        /// A material asset on the pipeline shader. The game builds its materials
        /// at runtime with Shader.Find, which a build may strip; having one real
        /// material asset keeps the shader in the build.
        static void EnsureDefaultMaterial(UniversalRenderPipelineAsset pipeline)
        {
            if (pipeline == null) return;
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (existing != null) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) return;

            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, DefaultMaterialPath);
        }
    }
}
