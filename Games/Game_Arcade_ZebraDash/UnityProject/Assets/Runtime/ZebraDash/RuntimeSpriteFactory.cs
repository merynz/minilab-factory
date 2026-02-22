using UnityEngine;

namespace ZebraDash
{
    public static class RuntimeSpriteFactory
    {
        private static Sprite whiteSprite;
        private static Texture2D whiteTexture;

        public static GameObject Create(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material = null,
            int sortingOrder = 0)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = GetWhiteSprite();
            renderer.sortingOrder = sortingOrder;
            if (material != null)
            {
                renderer.material = material;
            }

            return go;
        }

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite != null)
            {
                return whiteSprite;
            }

            if (whiteTexture == null)
            {
                whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTexture.name = "RuntimeWhiteTexture";
                whiteTexture.filterMode = FilterMode.Bilinear;
                whiteTexture.wrapMode = TextureWrapMode.Clamp;
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply(false, true);
            }

            whiteSprite = Sprite.Create(
                whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            whiteSprite.name = "RuntimeWhiteSprite";
            return whiteSprite;
        }
    }
}
