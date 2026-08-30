using UnityEngine;

namespace Mavis.Overlays
{
    /// <summary>程序生成的气泡/面板圆角底板 sprite（9-slice），全局共享。</summary>
    public static class BubbleArt
    {
        static Sprite _rounded;
        static Sprite _circle;

        /// <summary>实心圆 sprite（滑条手柄）。</summary>
        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, d <= size / 2f - 1f ? 1f : 0f));
                }
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 48f);
            _circle.name = "BubbleCircle";
            return _circle;
        }

        public static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;
            int size = 64, radius = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                    float cy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    float a = d >= radius ? 0f : Mathf.Clamp01((radius - d) / 1.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            _rounded.name = "BubbleRounded";
            return _rounded;
        }
    }
}
