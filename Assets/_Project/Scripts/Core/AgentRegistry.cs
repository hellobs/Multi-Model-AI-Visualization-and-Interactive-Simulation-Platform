using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Mavis.Agents;
using Mavis.Data;

namespace Mavis.Core
{
    /// <summary>
    /// 角色注册表：以消息里的 name 为键动态建角色（不硬编码名字）。
    /// 旧存档用中文名、资产目录用英文名，经 agent_alias.json 映射到贴图资产；
    /// 映射不到时按名字原样加载（真后端直接发英文名，无需映射）。
    /// </summary>
    public class AgentRegistry
    {
        public IReadOnlyDictionary<string, AgentController> Agents => _agents;

        /// <summary>场景中所有角色的统一父节点。</summary>
        public Transform AgentsRoot
        {
            get
            {
                if (_agentsRoot == null)
                {
                    var found = GameObject.Find("Agents");
                    _agentsRoot = found != null ? found.transform
                        : new GameObject("Agents").transform;
                }
                return _agentsRoot;
            }
        }
        Transform _agentsRoot;

        readonly Dictionary<string, AgentController> _agents = new Dictionary<string, AgentController>();
        readonly Dictionary<string, string> _alias = new Dictionary<string, string>();

        public void LoadAlias(string json)
        {
            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (map == null) return;
                _alias.Clear();
                foreach (var kv in map) _alias[kv.Key] = kv.Value;
                Debug.Log($"[AgentRegistry] 别名表加载: {map.Count} 条");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AgentRegistry] 别名表解析失败: {e.Message}");
            }
        }

        /// <summary>按消息名查已生成的角色（不创建），气泡定位用。</summary>
        public AgentController Get(string name)
        {
            return _agents.TryGetValue(name, out var ctrl) ? ctrl : null;
        }

        public AgentController GetOrCreate(string name)
        {
            if (_agents.TryGetValue(name, out var existing)) return existing;
            var prefab = Resources.Load<GameObject>("Agent");
            if (prefab == null)
            {
                Debug.LogError("[AgentRegistry] 缺少 Agent.prefab，请先执行菜单 MAVIS/Agents/Generate Assets");
                return null;
            }

            string assetName = ResolveAsset(name);
            var go = Object.Instantiate(prefab);
            go.name = $"Agent_{assetName}";
            go.transform.SetParent(AgentsRoot, false);
            var ctrl = go.GetComponent<AgentController>();
            ctrl.Init(assetName);
            _agents[name] = ctrl;
            Debug.Log($"[AgentRegistry] 生成角色: {name} (贴图: {assetName})");
            return ctrl;
        }

        string ResolveAsset(string name)
        {
            return _alias.TryGetValue(name, out var mapped) ? mapped : name;
        }

        /// <summary>重连/新连接追赶：直接贴齐坐标，不走移动。</summary>
        public void ApplySnapshot(WsSnapshotMsg snap)
        {
            if (snap?.agents == null) return;
            foreach (var kv in snap.agents)
            {
                var ctrl = GetOrCreate(kv.Key);
                if (ctrl == null) continue;
                var st = kv.Value;
                if (st != null && st.coord != null && st.coord.Length >= 2)
                    ctrl.SnapTo(new Vector2Int(st.coord[0], st.coord[1]));
            }
        }
    }
}
