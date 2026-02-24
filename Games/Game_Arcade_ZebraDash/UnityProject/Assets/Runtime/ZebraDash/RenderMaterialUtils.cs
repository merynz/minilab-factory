using UnityEngine;

namespace ZebraDash
{
    public static class RenderMaterialUtils
    {
        public static Material CreateSolidMaterial(Color color, bool transparent = false)
        {
            Shader shader = FindBestShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader);
            ApplyColor(material, color);

            if (transparent)
            {
                ConfigureTransparency(material);
            }

            return material;
        }

        public static void ApplyColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static Shader FindBestShader()
        {
            string[] names =
            {
                "Sprites/Default",
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Universal Render Pipeline/Lit",
                "Standard"
            };

            for (int i = 0; i < names.Length; i++)
            {
                Shader shader = Shader.Find(names[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void ConfigureTransparency(Material material)
        {
            // Works for URP Unlit/Lit and built-in fallback paths.
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
