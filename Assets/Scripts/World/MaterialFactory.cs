using UnityEngine;

namespace Foundation
{
    /// <summary>
    /// Creates one flat material per colour at runtime. No textures, no material
    /// assets, and it degrades gracefully: URP Lit if URP is active, Standard if
    /// the project is still on the Built-in pipeline, an unlit shader last.
    /// </summary>
    public static class MaterialFactory
    {
        static Shader _shader;
        static Material _template;
        static bool _templateChecked;

        public static Shader LitShader
        {
            get
            {
                if (_shader != null) return _shader;
                _shader = Shader.Find("Universal Render Pipeline/Lit");
                if (_shader == null) _shader = Shader.Find("Standard");
                if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (_shader == null) _shader = Shader.Find("Unlit/Color");
                return _shader;
            }
        }

        /// The editor setup script drops a material built on the active pipeline
        /// shader into Resources so the shader survives a build (a runtime
        /// Shader.Find alone can be stripped).
        static Material Template
        {
            get
            {
                if (!_templateChecked)
                {
                    _templateChecked = true;
                    _template = Resources.Load<Material>("FoundationDefaultMaterial");
                }
                return _template;
            }
        }

        public static Material Lit(Color color, float smoothness = 0.12f)
        {
            Material template = Template;
            Shader shader = template != null ? template.shader : LitShader;
            if (shader == null)
            {
                Debug.LogWarning("[MaterialFactory] No usable shader found; primitives will use their default material.");
                return null;
            }

            var material = _template != null ? new Material(_template) : new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            return material;
        }

        public static void Apply(Renderer renderer, Color color, float smoothness = 0.12f)
        {
            if (renderer == null) return;
            Material material = Lit(color, smoothness);
            if (material != null) renderer.sharedMaterial = material;
        }
    }
}
