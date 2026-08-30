using UnityEngine;
using Mavis.Core;

namespace Mavis.Overlays
{
    /// <summary>
    /// 模拟时钟：贴地图左上角，TextMesh 显示纯时间（如 10:42）。
    /// 实现要点（本工程实测）：
    /// 1. 运行时 AddComponent 的 TextMesh 字体为 null，此时 fontSize 等 setter 会 NRE，
    ///    因此每一步独立 try/catch；字体从场景内名牌 TextMesh 复制（名牌已验证可渲染）。
    /// 2. 名牌字体 = LegacyRuntime（预制体序列化），运行时引用可用，渲染数字零风险。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        TextMesh _clock;
        SpriteRenderer _bg;
        GameObject _bgGo;
        bool _styled;
        string _pending = "--:--";

        /// <summary>地图世界尺寸（世界单位），WorldPresenter 建图后设置。</summary>
        public static Vector2 MapSize = new Vector2(27f, 24f);

        public static Hud Create()
        {
            var go = new GameObject("Hud");
            return go.AddComponent<Hud>();
        }

        /// <summary>传入 dispatcher 自动跟随模拟时间。</summary>
        public Hud Bind(MessageDispatcher dispatcher)
        {
            dispatcher.Time += m => SetClock(m?.time);
            dispatcher.Snapshot += m => SetClock(m?.time);
            SetClock(dispatcher.CurrentTime);
            return this;
        }

        void Start()
        {
            BuildClock();
        }

        void BuildClock()
        {
            // step1: 底板
            try
            {
                var bgGo = new GameObject("ClockBG");
                bgGo.transform.SetParent(transform, false);
                bgGo.transform.position = new Vector3(2.4f, MapSize.y - 0.7f, 0f);
                _bgGo = bgGo;

                _bg = bgGo.AddComponent<SpriteRenderer>();
                _bg.sprite = MakeRoundedSprite();
                _bg.color = new Color(0.1f, 0.12f, 0.18f, 0.85f); // 深色底，白字可见
                _bg.sortingOrder = 600;
            }
            catch (System.Exception e) { Debug.LogError("[Hud] step1 底板失败: " + e); return; }

            // step2: 只添加组件，不碰属性（AddComponent 同帧访问属性会 NRE，Update 里再配置）
            _clock = _bgGo.AddComponent<TextMesh>();
        }

        void Update()
        {
            if (_clock == null || _styled) return;
            try
            {
                // 字体：从场景内名牌复制（AddComponent 下一帧访问安全）
                if (_clock.font == null)
                {
                    foreach (var tm in FindObjectsOfType<TextMesh>())
                    {
                        if (tm != _clock && tm.font != null)
                        {
                            _clock.font = tm.font;
                            _clock.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
                            break;
                        }
                    }
                    if (_clock.font == null) return; // 名牌还没生成
                }

                _clock.fontSize = 48;
                _clock.characterSize = 0.16f;
                _clock.anchor = TextAnchor.MiddleCenter;
                _clock.alignment = TextAlignment.Center;
                _clock.color = Color.white;
                _clock.text = _pending;
                _styled = true;
                FitToText();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Hud] Update 配置失败: " + e.Message);
                _styled = true; // 防止每帧刷错
            }
        }

        void FitToText()
        {
            if (_clock == null) return;
            var b = _clock.GetComponent<MeshRenderer>().bounds;
            _bgGo.transform.localScale = new Vector3(b.size.x + 0.6f, b.size.y + 0.3f, 1f);
        }

        void SetClock(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            // "20250213-10:42" → "10:42"
            int dash = raw.LastIndexOf('-');
            _pending = dash >= 0 && dash + 1 < raw.Length ? raw.Substring(dash + 1) : raw;
            if (_clock != null && _styled) _clock.text = _pending;
        }

        /// <summary>圆角底板（程序生成，与气泡同款）。</summary>
        static Sprite MakeRoundedSprite()
        {
            int w = 260, h = 64, r = 20;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Max(r - x, x - (w - 1 - r), 0f);
                    float cy = Mathf.Max(r - y, y - (h - 1 - r), 0f);
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, d >= r ? 0f : Mathf.Clamp01((r - d) / 1.5f)));
                }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));
            sprite.name = "ClockBG";
            return sprite;
        }
    }
}
