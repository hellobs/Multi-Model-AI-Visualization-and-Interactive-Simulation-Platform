using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mavis.Agents;

namespace Mavis.Overlays
{
    /// <summary>
    /// 头顶气泡（uGUI 世界空间 Canvas）：常驻显示角色当前行为（agent 消息的 action）；
    /// 对话到达时临时切换为台词，几秒后自动回到行为。圆角底板（程序生成 9-slice），
    /// 单行摘要，文本按像素自适应截断。
    /// 布局：气泡默认贴头顶，每帧做矩形避让——只有与别的气泡相交时才向上推，
    /// 冲突消失自动落回，因此始终跟随自己的角色。
    /// </summary>
    public class ChatBubble : MonoBehaviour
    {
        public Vector2 canvasSize = new Vector2(760f, 150f);
        public float canvasScale = 0.01f;
        public float baseHeight = 2.2f;
        [Tooltip("对话覆盖行为的时长（秒）")]
        public float chatOverrideSeconds = 5f;
        [Tooltip("摘要最大全角字符数（含说话人）")]
        public int maxChars = 40; // 两行容量（每行约 20 全角字）

        static readonly List<ChatBubble> Active = new List<ChatBubble>();
        static int _layoutFrame = -1;

        RectTransform _canvasRt;
        Text _text;
        string _actionText = "";
        Coroutine _revert;

        public static void ShowAction(AgentController agent, string text, Font font)
        {
            if (agent == null || string.IsNullOrEmpty(text)) return;
            var bubble = agent.GetComponent<ChatBubble>();
            if (bubble == null)
                bubble = agent.gameObject.AddComponent<ChatBubble>();
            bubble.EnsureNodes(font);
            bubble._actionText = Elide(text, bubble.maxChars);
            if (bubble._revert == null)
                bubble.SetContent(bubble._actionText);
        }

        public static void ShowChat(AgentController agent, string text, Font font)
        {
            if (agent == null || string.IsNullOrEmpty(text)) return;
            var bubble = agent.GetComponent<ChatBubble>();
            if (bubble == null)
                bubble = agent.gameObject.AddComponent<ChatBubble>();
            bubble.EnsureNodes(font);
            bubble.SetContent(Elide(text, bubble.maxChars));
            if (bubble._revert != null) bubble.StopCoroutine(bubble._revert);
            bubble._revert = bubble.StartCoroutine(bubble.RevertToAction());
        }

        IEnumerator RevertToAction()
        {
            yield return new WaitForSeconds(chatOverrideSeconds);
            _revert = null;
            if (!string.IsNullOrEmpty(_actionText))
                SetContent(_actionText);
        }

        void EnsureNodes(Font font)
        {
            if (_canvasRt != null) return;

            var go = new GameObject("BubbleCanvas");
            go.transform.SetParent(transform, false);
            _canvasRt = go.AddComponent<Canvas>().GetComponent<RectTransform>();
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;
            _canvasRt.sizeDelta = canvasSize;
            _canvasRt.pivot = new Vector2(0.5f, 0f); // 底边中点对准头顶
            go.transform.localScale = Vector3.one * canvasScale;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(_canvasRt, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var img = bgGo.AddComponent<Image>();
            img.sprite = BubbleArt.RoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = new Color(1f, 1f, 1f, 0.92f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_canvasRt, false);
            _text = textGo.AddComponent<Text>();
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(24f, 4f);
            rt.offsetMax = new Vector2(-24f, -4f);
            _text.alignment = TextAnchor.MiddleLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap; // 自动换行，最多两行（超出截断）
            _text.verticalOverflow = VerticalWrapMode.Truncate;
            _text.color = new Color(0.12f, 0.12f, 0.12f);
            try { _text.font = font; }
            catch (System.Exception e) { Debug.LogWarning("[ChatBubble] 字体赋值失败(回退内置): " + e.Message); }
            _text.fontSize = 30;

            go.SetActive(false);
        }

        void SetContent(string content)
        {
            _text.text = content;
            _canvasRt.gameObject.SetActive(true);
            _canvasRt.sizeDelta = canvasSize; // 固定两行气泡
        }

        void LateUpdate() => ResolveLayout();

        /// <summary>
        /// 每帧一次的布局避让：气泡先落到各自头顶，再迭代检查矩形相交、向上推开。
        /// 因为是每帧重算，角色移动/冲突消失后气泡自动回到头顶。
        /// </summary>
        static void ResolveLayout()
        {
            if (_layoutFrame == Time.frameCount || Active.Count == 0) return;
            _layoutFrame = Time.frameCount;

            // 期望位置：各自头顶（气泡高度按各自实际尺寸）
            var rects = new List<Rect>(Active.Count);
            var bottoms = new List<Vector3>(Active.Count);
            foreach (var b in Active)
            {
                var bottom = b.transform.parent.position + Vector3.up * b.baseHeight;
                bottoms.Add(bottom);
                rects.Add(new Rect(bottom.x - b.Width() * 0.5f, bottom.y, b.Width(), b.Height()));
            }

            // 迭代避让：与已放置的矩形相交则向上推（两轮，减少级联）
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Active.Count; i++)
                {
                    var r = rects[i];
                    foreach (var p in rects)
                    {
                        if (p.Equals(r)) continue;
                        if (r.Overlaps(p))
                            r.y = p.yMax + 0.05f;
                    }
                    rects[i] = r;
                }
            }

            for (int i = 0; i < Active.Count; i++)
                Active[i]._canvasRt.position = new Vector3(rects[i].x + rects[i].width * 0.5f,
                    rects[i].y, bottoms[i].z);
        }

        float Width() => _canvasRt != null ? _canvasRt.sizeDelta.x * canvasScale : 2f;

        float Height() => _canvasRt != null ? _canvasRt.sizeDelta.y * canvasScale : 1f;

        void OnDestroy()
        {
            Active.Remove(this);
        }

        static string Elide(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }
}
