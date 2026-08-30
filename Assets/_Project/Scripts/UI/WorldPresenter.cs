using System.Collections;
using UnityEngine;
using Mavis.Core;
using Mavis.Agents;
using Mavis.Data;

namespace Mavis.Overlays
{
    /// <summary>
    /// 世界表现层装配：自举后订阅 MessageDispatcher，
    /// chat_line → 角色头顶气泡（单行摘要，层叠不重叠）；story → 顶部横幅；并挂相机跟随。
    /// 字体用打包的 NotoSansSC（动态字体，WebGL 运行时生成字形，非系统字体）。
    /// </summary>
    public class WorldPresenter : MonoBehaviour
    {
        Font _font;
        StoryBanner _banner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindObjectOfType<WorldPresenter>() != null) return;
            var go = new GameObject("WorldPresenter");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldPresenter>();
        }

        IEnumerator WaitForClient()
        {
            while (SimulationClient.Instance == null)
                yield return null;
            var client = SimulationClient.Instance;

            _font = Resources.Load<Font>("Fonts/NotoSansSC-Regular");
            if (_font == null)
                Debug.LogWarning("[WorldPresenter] 缺少 Fonts/NotoSansSC-Regular，中文将用默认字体渲染");

            _banner = StoryBanner.Create(_font);
            Hud.Create().Bind(client.Dispatcher);
            var mapLoader = FindObjectOfType<TiledMapLoader>();
            var rig = CameraRig.Create(client.Registry, mapLoader);
            if (mapLoader != null && mapLoader.Map != null)
                Hud.MapSize = new Vector2(mapLoader.Map.width, mapLoader.Map.height);

            client.Dispatcher.Agent += OnAgent;
            client.Dispatcher.ChatLine += OnChatLine;
            client.Dispatcher.Story += m => { if (m != null) _banner.Show(m); };
            Debug.Log("[WorldPresenter] 表现层就绪（气泡/横幅/相机跟随）");
        }

        void Awake() => StartCoroutine(WaitForClient());

        /// <summary>行为气泡：每个角色常驻显示最新 action。</summary>
        void OnAgent(WsAgentMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.name) || string.IsNullOrEmpty(msg.action)) return;
            var agent = SimulationClient.Instance != null
                ? SimulationClient.Instance.Registry.Get(msg.name)
                : null;
            if (agent == null) return;
            string text = msg.action.Replace("\n", " ");
            ChatBubble.ShowAction(agent, text, _font); // 名牌已显示名字，气泡只放内容
        }

        void OnChatLine(WsChatLineMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.speaker)) return;
            var agent = SimulationClient.Instance != null
                ? SimulationClient.Instance.Registry.Get(msg.speaker)
                : null;
            if (agent == null)
            {
                Debug.LogWarning($"[WorldPresenter] 说话者无角色实例: {msg.speaker}");
                return;
            }
            Debug.Log($"[WorldPresenter] 气泡 → {msg.speaker}: {((msg.text ?? "").Length > 30 ? (msg.text ?? "").Substring(0,30) : msg.text)}");
            // 台词里的换行折叠为空格（气泡是单行摘要）
            string text = (msg.text ?? "").Replace("\n", " ");
            // 文本可能自带说话人前缀（半角/全角冒号），剥掉；名牌已显示名字，气泡只放内容
            if (text.StartsWith(msg.speaker))
                text = text.Substring(msg.speaker.Length).TrimStart('：', ':', ' ');
            ChatBubble.ShowChat(agent, text, _font);
        }
    }
}
