using UnityEditor;
using UnityEngine;

namespace Mavis.EditorTools
{
    /// <summary>
    /// 表现层调试工具：向 MessageDispatcher 注入假消息，验证气泡/横幅渲染链路。
    /// </summary>
    public static class OverlayTester
    {
        [MenuItem("Mavis/Dev/注入测试对话气泡")]
        public static void FeedTestChat()
        {
            var client = SimulationClient();
            if (client == null) { Debug.LogError("[OverlayTester] SimulationClient 未就绪（需在 Play 模式）"); return; }
            client.Dispatcher.Feed("{\"type\":\"chat_line\",\"speaker\":\"AI Advisor\",\"text\":\"测试气泡：市场波动加剧，请注意风险控制。\",\"time\":\"20250213-11:00\"}");
            Debug.Log("[OverlayTester] 已注入测试 chat_line");
        }

        [MenuItem("Mavis/Dev/注入测试行为气泡")]
        public static void FeedTestAction()
        {
            var client = SimulationClient();
            if (client == null) { Debug.LogError("[OverlayTester] SimulationClient 未就绪（需在 Play 模式）"); return; }
            client.Dispatcher.Feed("{\"type\":\"agent\",\"name\":\"AI Advisor\",\"role_type\":\"ai_tool\",\"action\":\"测试行为气泡：正在交叉核验财报数据。\"}");
            client.Dispatcher.Feed("{\"type\":\"agent\",\"name\":\"Wendy Lin\",\"role_type\":\"user\",\"action\":\"测试行为气泡：整理合规清单并汇报。\"}");
            Debug.Log("[OverlayTester] 已注入测试 agent 行为");
        }

        [MenuItem("Mavis/Dev/注入测试剧情横幅")]
        public static void FeedTestStory()
        {
            var client = SimulationClient();
            if (client == null) { Debug.LogError("[OverlayTester] SimulationClient 未就绪（需在 Play 模式）"); return; }
            client.Dispatcher.Feed("{\"type\":\"story\",\"id\":\"t1\",\"time\":\"20250213-11:00\",\"event_type\":\"Test Event\",\"content\":\"A wealthy client asks about solar stocks.\",\"targets\":[\"AI Advisor\",\"Wendy Lin\"]}");
            Debug.Log("[OverlayTester] 已注入测试 story");
        }

        static Mavis.Core.SimulationClient SimulationClient()
        {
            return Object.FindObjectOfType<Mavis.Core.SimulationClient>();
        }
    }
}
