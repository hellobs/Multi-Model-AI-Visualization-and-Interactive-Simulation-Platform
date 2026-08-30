using System.Collections;
using UnityEngine;
using Mavis.Core;
using Mavis.Data;

namespace Mavis.Overlays
{
    /// <summary>
    /// 剧情事件横幅：顶部居中，深红底白字，单行摘要【事件类型】内容 + 影响对象。
    /// 照 provenance story-banner：闪烁三次后保持显示 12 秒，但尺寸收敛在画面内
    /// （4K UHD 下字高约 0.55 单位 ≈ 50px），底板与文本互为兄弟节点并钳制宽度。
    /// </summary>
    public class StoryBanner : MonoBehaviour
    {
        public float displaySeconds = 12f;
        [Tooltip("标题字形高度（世界单位）")]
        public float glyphHeight = 0.55f;
        [Tooltip("单行最大字符数")]
        public int maxCharsPerLine = 38;

        const float MaxBannerWidth = 19f; // 4K 16:9 可视宽约 21.3 单位，留边

        TextMesh _text;
        Transform _textT, _bg;
        SpriteRenderer _bgSr;

        public static StoryBanner Create(Font font)
        {
            var root = new GameObject("StoryBanner");
            var banner = root.AddComponent<StoryBanner>();
            banner.Build(font);
            return banner;
        }

        void Build(Font font)
        {
            var bgGo = new GameObject("BannerBG");
            bgGo.transform.SetParent(transform, false);
            _bgSr = bgGo.AddComponent<SpriteRenderer>();
            _bgSr.sprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            _bgSr.color = new Color(0.55f, 0.12f, 0.12f, 0.92f);
            _bgSr.sortingOrder = 900;
            _bg = bgGo.transform;
            _bg.gameObject.SetActive(false);

            var textGo = new GameObject("BannerText");
            textGo.transform.SetParent(transform, false); // 兄弟节点
            _textT = textGo.transform;
            _text = textGo.AddComponent<TextMesh>();
            _text.fontSize = 64;
            _text.characterSize = glyphHeight / 10f;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = Color.white;
            _text.font = font;
            var tmr = _text.GetComponent<MeshRenderer>();
            tmr.sharedMaterial = font != null ? font.material : null;
            tmr.sortingOrder = 901;
            _textT.gameObject.SetActive(false);
        }

        public void Show(WsStoryMsg msg)
        {
            if (_text == null) return;
            string type = string.IsNullOrEmpty(msg.event_type) ? "event" : msg.event_type;
            string title = Elide($"【{type}】{msg.content}", maxCharsPerLine);
            string affect = msg.targets != null && msg.targets.Count > 0
                ? "\n影响: " + Elide(string.Join("、", msg.targets), 22)
                : "";
            _text.text = title + affect;
            _textT.localPosition = Vector3.zero;
            _bg.localScale = Vector3.one;
            _textT.localScale = Vector3.one;
            FitBackground();
            _bg.gameObject.SetActive(true);
            _textT.gameObject.SetActive(true);
            SetAlpha(1f);
            StopAllCoroutines();
            StartCoroutine(LifeCycle());
        }

        void FitBackground()
        {
            Bounds b = _text.GetComponent<MeshRenderer>().bounds;
            float w = Mathf.Clamp(b.size.x + 0.8f, 3f, MaxBannerWidth);
            float h = Mathf.Clamp(b.size.y + 0.3f, glyphHeight + 0.3f, 2f);
            _bg.localScale = new Vector3(w, h, 1f);
            _textT.localPosition = Vector3.zero;
        }

        void LateUpdate()
        {
            if (_bg == null || !_bg.gameObject.activeInHierarchy) return;
            FitBackground();
            var cam = Camera.main;
            if (cam != null)
            {
                Bounds b = _text.GetComponent<MeshRenderer>().bounds;
                float halfH = Mathf.Max(b.size.y, glyphHeight) * 0.5f + 0.25f;
                var p = transform.position;
                p.x = cam.transform.position.x;
                p.y = cam.transform.position.y + cam.orthographicSize - halfH;
                transform.position = p;
            }
        }

        IEnumerator LifeCycle()
        {
            for (int i = 0; i < 3; i++)
            {
                SetAlpha(0.35f); yield return new WaitForSeconds(0.25f);
                SetAlpha(1f); yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(displaySeconds - 1.5f);
            float t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(1f, 0f, t / 0.6f));
                yield return null;
            }
            _bg.gameObject.SetActive(false);
            _textT.gameObject.SetActive(false);
        }

        void SetAlpha(float a)
        {
            if (_bgSr != null)
            {
                var c = _bgSr.color; c.a = a * 0.92f; _bgSr.color = c;
            }
            if (_text != null)
            {
                var c = _text.color; c.a = a; _text.color = c;
            }
        }

        static string Elide(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
