using UnityEngine;

namespace ZebraDash
{
    public enum RuntimeShapeKind
    {
        Square = 0,
        Diamond = 1,
        TriangleUp = 2,
        TriangleRight = 3,
        BarHorizontal = 4,
        BarVertical = 5,
        Ring = 6,
        Cross = 7,
        Slash = 8,
        Chevron = 9,
        Capsule = 10,
        Spike = 11
    }

    public static class RuntimeSpriteFactory
    {
        private static Sprite whiteSprite;
        private static Texture2D whiteTexture;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> GeneratedSprites =
            new System.Collections.Generic.Dictionary<string, Sprite>(System.StringComparer.Ordinal);

        public static GameObject Create(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Sprite sprite = null,
            Material material = null,
            int sortingOrder = 0,
            Color? color = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite ?? GetWhiteSprite();
            renderer.sortingOrder = sortingOrder;
            if (color.HasValue)
            {
                renderer.color = color.Value;
            }

            if (material != null)
            {
                renderer.material = material;
            }

            return go;
        }

        public static Sprite GetShapeSprite(RuntimeShapeKind shape, int size = 64)
        {
            int clampedSize = Mathf.Clamp(size, 16, 128);
            string key = $"{shape}_{clampedSize}";
            if (GeneratedSprites.TryGetValue(key, out Sprite existing) && existing != null)
            {
                return existing;
            }

            Texture2D texture = new Texture2D(clampedSize, clampedSize, TextureFormat.RGBA32, false);
            texture.name = $"Runtime_{key}";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color clear = new Color(1f, 1f, 1f, 0f);
            Color solid = Color.white;
            for (int y = 0; y < clampedSize; y++)
            {
                for (int x = 0; x < clampedSize; x++)
                {
                    texture.SetPixel(x, y, clear);
                }
            }

            DrawShape(texture, shape, solid);
            texture.Apply(false, true);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, clampedSize, clampedSize),
                new Vector2(0.5f, 0.5f),
                clampedSize);
            sprite.name = $"RuntimeSprite_{key}";
            GeneratedSprites[key] = sprite;
            return sprite;
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

        private static void DrawShape(Texture2D texture, RuntimeShapeKind shape, Color color)
        {
            int size = texture.width;
            int c = size / 2;
            int r = Mathf.Max(3, (size / 2) - 2);
            int thickness = Mathf.Max(2, size / 10);

            switch (shape)
            {
                case RuntimeShapeKind.Square:
                    FillRect(texture, size / 6, size / 6, size - (size / 3), size - (size / 3), color);
                    break;

                case RuntimeShapeKind.Diamond:
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            int manhattan = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                            if (manhattan <= r)
                            {
                                texture.SetPixel(x, y, color);
                            }
                        }
                    }
                    break;

                case RuntimeShapeKind.TriangleUp:
                    for (int y = 0; y < size; y++)
                    {
                        float yn = y / (float)(size - 1);
                        int half = Mathf.RoundToInt((1f - yn) * (size * 0.40f));
                        int minX = Mathf.Clamp(c - half, 0, size - 1);
                        int maxX = Mathf.Clamp(c + half, 0, size - 1);
                        for (int x = minX; x <= maxX; x++)
                        {
                            texture.SetPixel(x, y, color);
                        }
                    }
                    break;

                case RuntimeShapeKind.TriangleRight:
                    for (int x = 0; x < size; x++)
                    {
                        float xn = x / (float)(size - 1);
                        int half = Mathf.RoundToInt((1f - xn) * (size * 0.40f));
                        int minY = Mathf.Clamp(c - half, 0, size - 1);
                        int maxY = Mathf.Clamp(c + half, 0, size - 1);
                        for (int y = minY; y <= maxY; y++)
                        {
                            texture.SetPixel(x, y, color);
                        }
                    }
                    break;

                case RuntimeShapeKind.BarHorizontal:
                    FillRect(texture, size / 8, (size / 2) - thickness, size - (size / 4), thickness * 2, color);
                    break;

                case RuntimeShapeKind.BarVertical:
                    FillRect(texture, (size / 2) - thickness, size / 8, thickness * 2, size - (size / 4), color);
                    break;

                case RuntimeShapeKind.Ring:
                    int outer = r;
                    int inner = Mathf.Max(1, r - Mathf.Max(2, size / 8));
                    int outerSq = outer * outer;
                    int innerSq = inner * inner;
                    for (int y = 0; y < size; y++)
                    {
                        int dy = y - c;
                        for (int x = 0; x < size; x++)
                        {
                            int dx = x - c;
                            int d = (dx * dx) + (dy * dy);
                            if (d <= outerSq && d >= innerSq)
                            {
                                texture.SetPixel(x, y, color);
                            }
                        }
                    }
                    break;

                case RuntimeShapeKind.Cross:
                    FillRect(texture, (size / 2) - thickness, size / 8, thickness * 2, size - (size / 4), color);
                    FillRect(texture, size / 8, (size / 2) - thickness, size - (size / 4), thickness * 2, color);
                    break;

                case RuntimeShapeKind.Slash:
                    DrawDiagonalBand(texture, color, thickness, true);
                    break;

                case RuntimeShapeKind.Chevron:
                    DrawChevron(texture, color, thickness);
                    break;

                case RuntimeShapeKind.Capsule:
                    DrawCapsule(texture, color, thickness);
                    break;

                case RuntimeShapeKind.Spike:
                    DrawSpike(texture, color);
                    break;

                default:
                    FillRect(texture, size / 6, size / 6, size - (size / 3), size - (size / 3), color);
                    break;
            }
        }

        private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color color)
        {
            int maxX = Mathf.Min(texture.width, x + width);
            int maxY = Mathf.Min(texture.height, y + height);
            for (int py = Mathf.Max(0, y); py < maxY; py++)
            {
                for (int px = Mathf.Max(0, x); px < maxX; px++)
                {
                    texture.SetPixel(px, py, color);
                }
            }
        }

        private static void DrawDiagonalBand(Texture2D texture, Color color, int halfThickness, bool positiveSlope)
        {
            int size = texture.width;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int target = positiveSlope ? x : (size - 1 - x);
                    if (Mathf.Abs(y - target) <= halfThickness)
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static void DrawChevron(Texture2D texture, Color color, int halfThickness)
        {
            int size = texture.width;
            int mid = size / 2;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int leftLine = Mathf.RoundToInt((y * 0.65f) + (size * 0.10f));
                    int rightLine = Mathf.RoundToInt(((size - 1 - y) * 0.65f) + (size * 0.10f));
                    bool nearLeft = Mathf.Abs(x - leftLine) <= halfThickness;
                    bool nearRight = Mathf.Abs(x - rightLine) <= halfThickness;
                    if ((nearLeft || nearRight) && x <= mid + (size / 5))
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static void DrawCapsule(Texture2D texture, Color color, int thickness)
        {
            int size = texture.width;
            int radius = Mathf.Max(3, (size / 2) - thickness);
            int leftCx = radius;
            int rightCx = size - 1 - radius;
            int cy = size / 2;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inCenter = x >= leftCx && x <= rightCx && Mathf.Abs(y - cy) <= radius;
                    int dxL = x - leftCx;
                    int dxR = x - rightCx;
                    int dy = y - cy;
                    bool inLeft = (dxL * dxL) + (dy * dy) <= radius * radius;
                    bool inRight = (dxR * dxR) + (dy * dy) <= radius * radius;
                    if (inCenter || inLeft || inRight)
                    {
                        texture.SetPixel(x, y, color);
                    }
                }
            }
        }

        private static void DrawSpike(Texture2D texture, Color color)
        {
            int size = texture.width;
            int c = size / 2;
            for (int y = 0; y < size; y++)
            {
                float yn = y / (float)(size - 1);
                int half = Mathf.RoundToInt((1f - yn) * (size * 0.20f));
                int minX = Mathf.Clamp(c - half, 0, size - 1);
                int maxX = Mathf.Clamp(c + half, 0, size - 1);
                for (int x = minX; x <= maxX; x++)
                {
                    texture.SetPixel(x, y, color);
                }
            }

            for (int y = 0; y < size; y++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(c, size - 1, y / (float)(size - 1)));
                for (int t = -1; t <= 1; t++)
                {
                    int px = x + t;
                    if (px >= 0 && px < size)
                    {
                        texture.SetPixel(px, y, color);
                    }
                }
            }
        }
    }
}
