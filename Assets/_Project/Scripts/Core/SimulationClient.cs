using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Mavis.Agents;
using Mavis.Data;
using Mavis.Network;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace Mavis.Core
{
    /// <summary>
    /// 自举组件：运行时自动创建（不改场景文件，与地图任务零冲突）。
    /// 持有 WSClient / MessageDispatcher / AgentRegistry，把消息流接到角色系统。
    /// </summary>
    public class SimulationClient : MonoBehaviour
    {
        public string backendUrl = "ws://localhost:5001/ws";

        public static SimulationClient Instance { get; private set; }

        /// <summary>消息分发器（表现层订阅用）。</summary>
        public MessageDispatcher Dispatcher => _dispatcher;
        /// <summary>角色注册表（气泡定位、相机跟随目标用）。</summary>
        public AgentRegistry Registry => _registry;

        WSClient _ws;
        readonly MessageDispatcher _dispatcher = new MessageDispatcher();
        readonly AgentRegistry _registry = new AgentRegistry();
        TiledMapLoader _mapLoader;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (FindObjectOfType<SimulationClient>() != null) return;
            var go = new GameObject("SimulationClient");
            go.AddComponent<SimulationClient>();
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            // 测试/演示可切后端，优先级：PlayerPrefs > 环境变量 > 默认
            // （PlayerPrefs 可让已运行的编辑器不改代码即切后端；发布版无副作用）
            string prefUrl = PlayerPrefs.GetString("MAVIS_BACKEND", "");
            if (!string.IsNullOrEmpty(prefUrl)) backendUrl = prefUrl;
            string envUrl = Environment.GetEnvironmentVariable("MAVIS_BACKEND");
            if (string.IsNullOrEmpty(prefUrl) && !string.IsNullOrEmpty(envUrl)) backendUrl = envUrl;
            backendUrl = ResolveUrl(backendUrl);

            _dispatcher.Agent += OnAgent;
            _dispatcher.Snapshot += OnSnapshot;
            _dispatcher.Init += () => Debug.Log("[Sim] 已连接(init)");
            _dispatcher.Time += m => Debug.Log($"[Sim] 模拟时间 → {m?.time}");
            _dispatcher.ChatLine += m => Debug.Log($"[Chat] {m?.speaker}: {Trunc(m?.text, 60)}");
            _dispatcher.Story += m => Debug.Log($"[Story] 【{m?.event_type}】{Trunc(m?.content, 50)} → {(m?.targets == null ? "" : string.Join(",", m.targets))}");
            _dispatcher.Done += () => Debug.Log("[Sim] 模拟结束(done)");
            _dispatcher.Error += msg => Debug.LogError($"[Sim] 后端错误: {msg}");

            StartCoroutine(LoadAliasThenConnect());
        }

        void OnAgent(WsAgentMsg m)
        {
            var ctrl = _registry.GetOrCreate(m.name);
            if (ctrl == null) return;
            ctrl.SetRoleType(m.role_type);
            ctrl.OnAgentState(m);
            Debug.Log($"[Agent] {(m.role_type == "ai_tool" ? "[AI] " : "")}{m.name} → {Trunc(m.action, 30)} @ {(m.coord != null && m.coord.Length >= 2 ? $"({m.coord[0]},{m.coord[1]})" : "(?)")}");
        }

        void OnSnapshot(WsSnapshotMsg s)
        {
            _registry.ApplySnapshot(s);
            Debug.Log($"[Sim] Snapshot 追赶: {s?.agents?.Count ?? 0} agents @ {s?.time}");
        }

        IEnumerator LoadAliasThenConnect()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var req = UnityWebRequest.Get(Path.Combine(Application.streamingAssetsPath, "agent_alias.json"));
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
                _registry.LoadAlias(req.downloadHandler.text);
#else
            string p = Path.Combine(Application.streamingAssetsPath, "agent_alias.json");
            if (File.Exists(p)) _registry.LoadAlias(File.ReadAllText(p));
#endif
            _ws = new WSClient(backendUrl);
            _ws.OnMessageRaw += _dispatcher.Feed;
            _ws.OnStateChanged += s => Debug.Log($"[WS] 状态: {s}");
            _ws.Connect();
            yield break;
        }

        void Update()
        {
            _ws?.Pump();
            SyncMapHeight();
        }

        // 地图任务可能后于本组件加载，周期性把 TiledMapLoader 的地图高度同步给 MapCoords
        void SyncMapHeight()
        {
            if (_mapLoader == null && Time.frameCount % 60 == 0)
                _mapLoader = FindObjectOfType<TiledMapLoader>();
            var map = _mapLoader != null ? _mapLoader.Map : null;
            if (map != null && map.height > 0) MapCoords.SetMapHeight(map.height);
        }

        // WebGL 嵌 iframe 时后端地址用页面参数传入，如 page.html?backend=ws://host:5001/ws
        string ResolveUrl(string def)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string url = Application.absoluteURL;
            int i = url.IndexOf("backend=", StringComparison.Ordinal);
            if (i >= 0)
            {
                string v = url.Substring(i + 8);
                int end = v.IndexOf('&');
                if (end >= 0) v = v.Substring(0, end);
                if (v.Length > 0) return v;
            }
#endif
            return def;
        }

        static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }

        void OnDestroy() { _ws?.Dispose(); }
    }
}