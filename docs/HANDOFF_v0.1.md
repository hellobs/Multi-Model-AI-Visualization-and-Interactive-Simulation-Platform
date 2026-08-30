# MAVIS Unity · v0.1 交接文档（agent 消息驱动移动线）

> 写于 2026-08-29。上一个会话完成了 v0.1 的素材迁移 + 网络层 + 角色系统代码，
> 本文交接给下一个执行者。**地图（Tiled 加载）是另一条任务线，本文所有工作都不要碰地图文件。**
>
> 权威上下文：工程根 `CLAUDE.md`（v0.3）、`docs/DEVELOPMENT_PLAN.md`。
> 消息契约唯一真相：`D:\zzr\provenance\provenance\live_fastapi.py` + `D:\zzr\mavis\mavisframework\runtime\protocol.py` + Phaser 参照 `D:\zzr\provenance\provenance\frontend\templates\main_script.html`。

---

## 0. v0.1 目标与边界（一句话）

**6 个 agent 由后端 WS 消息驱动，在地图上按 path 逐格走动**（含手动 4 方向帧动画、头顶名牌、断线重连）。
不做：chat 面板、治理约束面板、倾向可视化 UI、时钟/横幅、中文字体、WebGL 打包。
数据源双路：**真后端（live_fastapi + LLM）为主，存档回放器（tools/mock_ws_server.py）做开发/保底**，两者喂同一契约。

## 1. 已完成的工作（全部已落盘，未 git 提交）

### 1.1 已拷贝素材（从 provenance）

| 目标路径 | 内容 | 说明 |
|---|---|---|
| `Assets/_Project/Resources/Agents/<名>/texture.png` | 6 个角色的行走图 | 名字：`AI Advisor` `Daniel Shen` `Kevin Su` `Michael Chen` `Mr. Zhou` `Wendy Lin`。**全部 96×128 = 3列×4行、每格 32×32**，行序 down/left/right/up（对应 sprite.json y=0/32/64/96），列 0/1/2；走路序列 `[0,1,2,1]`，站立帧=列 1 |
| `Assets/_Project/Art/Agents/<名>/{portrait.png, agent.json}` | 头像+人设配置 | v0.1 不用，后续 UI 备用 |
| `Assets/_Project/Art/Agents/sprite.json` | 帧坐标参照 | 上面布局的出处 |

放在 `Resources/` 下的原因：运行时用 `Resources.LoadAll<Sprite>("Agents/<名>/texture")` 按切片名加载，不需要 Addressables/AssetDatabase。

### 1.2 已写代码（7 个文件，均可编译，命名空间 `ZZR.*` 与地图任务一致）

| 文件 | 职责 | 关键点 |
|---|---|---|
| `Assets/StreamingAssets/agent_alias.json` | 中文名 → 英文资产名映射 | **旧存档（gtc-demo14 等，8/28 生成）里角色名是中文**（AI投顾助手/沈砚之/苏清越/陈慕白/林晚晴/老周），**资产目录与真后端（现在）用英文名**。回放旧档必须靠这张表；真后端英文名直接命中资产，不经过它 |
| `Assets/_Project/Scripts/Data/WsMessages.cs` | 8 类消息契约 C# POCO | 字段以 live_fastapi.py L119–136 实发为准：`agent` 含 `name/coord/path/action/location/currently/role_type/goal_score/goal_alignment/value_tendency/time/conversation/description`；`snapshot` = `{type,time,agents:{name:AgentState}}`（mavis protocol.py 确认过）。八类**全部解析**，v0.1 只消费 agent/snapshot，其余进日志——之后加面板不用动网络层 |
| `Assets/_Project/Scripts/Data/MapCoords.cs` | 格子↔世界坐标换算 | **与地图任务 TiledMapLoader 约定对齐**：Tiled row0 在顶、Unity y 向上，`world=(x+0.5, mapHeight-1-y+0.5)`；`MapHeight` 由 SimulationClient 从 `TiledMapLoader.Map.height` 同步，加载前兜底 24（tilemap.json 实际 27×24） |
| `Assets/_Project/Scripts/Network/WSClient.cs` | WS 客户端 | NativeWebSocket（WebGL 兼容）。**主线程 `Pump()` 派发收包**（WebGL 无线程）。看门狗照 Phaser：服务端 5s 一 ping，**20s 无消息判死**→强制关闭→3s 后重连；**10s 窗口内超 6 次重连判服务不可用**，停止重试（防死循环）。状态机 Idle/Connecting/Open/WaitingReconnect/Failed |
| `Assets/_Project/Scripts/Core/MessageDispatcher.cs` | 解析+分发 | `Feed(json)`→JObject→按 type 分发 C# 事件。**倾向历史缓冲已内置**（按模拟时间去重、同时间刷新、上限 200 点，照 main_script `tendencyRecord`）——`TendencyHistory` 字典直接就是将来曲线 UI 的数据源 |
| `Assets/_Project/Scripts/Core/AgentRegistry.cs` | name→角色 动态注册 | 收到首条 agent 消息才 `Instantiate`（`Resources.Load("Agent")`），**不硬编码角色名**；`ApplySnapshot` 直接贴齐坐标（重连追赶） |
| `Assets/_Project/Scripts/Agents/AgentController.cs` | 角色视觉 | **手动帧动画**（不用 Animator 状态机，直接切 SpriteRenderer.sprite，行为与 Phaser anims 一致且零资产依赖）：4 方向×[0,1,2,1]@10fps，站立=列1帧；名牌用 **TextMesh**（内置 LegacyRuntime 字体，v0.1 全英文显示，避开中文字体依赖；ai_tool 角色蓝字）；贴图缺失时洋红方块占位并报错。`OnAgentState`：首条消息 SnapTo(coord)，有 path 走 path，无 path 走向 coord |
| `Assets/_Project/Scripts/Agents/AgentMovement.cs` | 移动（Phaser 逐条翻译） | waypoint 队列：新 path **追加队尾**（先走完旧路）、首点=队尾时去重；恒速 **1.5 格/s**（=Phaser 48px/s）；**偏差>3 格直接贴齐**（丢消息/重连恢复，防穿墙）；按位移主轴切方向动画；走完回站立帧 |

### 1.3 已做杂项

- `.gitignore` 追加了 `__pycache__/`、`*.pyc`（docs 下有 python 缓存）。
- **git 未做任何提交**（仓库在工程根，remote=github hellobs/…，分支 main）。用户要求分阶段提交，且**地图相关文件不要由本线提交**（见 §5）。

### 1.4 已确认的环境事实

- Unity MCP **在线**（Editor 开着工程）：HTTP JSON-RPC `POST http://localhost:8080/mcp`，调用辅助脚本 `D:\zzr\tools\mcp_call.py`（用法 `python mcp_call.py <tool> '<json>'`）。111 个工具，常用的：`editor_control`(force_refresh/force_recompile)、`wait_for_compilation`(status/wait)、`execute_menu_item`、`read_console`(get_errors)、`play_mode_control`(play/stop)、`screenshot`(game_view)、`manage_script`、`upm_control`。
- provenance 真后端启动：`cd D:\zzr\provenance\provenance && python live_fastapi.py --name demo --start "20250213-09:30" --stride 2 --step 0 --port 5001`。
- 回放数据：`D:\zzr\provenance\provenance\results\checkpoints\gtc-demo14\`（55 档 `simulate-*.json`，每档=2 分钟，含 6 角色 `coord/action/currently/status.value_tendency/goal_alignment`；**path 为 null**——回放器省略 path 字段即可，客户端对无 path 消息直接走向 coord（Phaser 同款行为）；**存档里 action 是 dict** `{"event":{subject,predicate,object}}`，回放器要拼成字符串）+ `conversation.json`（键=时间如 `20250213-10:04`，值=[{地点头:[[speaker,line],…]},…]）+ `decisions.json`。
- story 事件：`D:\zzr\provenance\provenance\scenarios\investment\story.json`，事件 `time` 是 `"09:40"` 形式，与步时间 `20250213-09:40` 的 `[9:]` 后缀匹配。

## 2. ⚠️ 立即要做的第一件事：装 NativeWebSocket

`WSClient.cs` 引用了 `NativeWebSocket` 命名空间，**不装包整个工程编译不过**。

`Packages/manifest.json` 的 `dependencies` 里加一行（已有 Newtonsoft 3.0.2 不用动）：

```json
"com.endel.nativewebsocket": "https://github.com/endel/NativeWebSocket.git#upm"
```

用 MCP 执行：`editor_control` action=force_refresh → `wait_for_compilation` action=wait → `read_console` get_errors。若 git 拉包失败（网络/代理），降级方案：从 https://github.com/endel/NativeWebSocket 把 `NativeWebSocket.cs`（Runtime 目录下单文件，MIT）下载放进 `Assets/_Project/Plugins/NativeWebSocket/`，去掉 manifest 那行即可。

## 3. 还没写的 3 个文件（完整代码直接粘贴）

### 3.1 `Assets/_Project/Scripts/Core/SimulationClient.cs`（自举+总装）

```csharp
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using ZZR.Agents;
using ZZR.Data;
using ZZR.Network;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace ZZR.Core
{
    /// <summary>
    /// 自举组件：运行时自动创建（不改场景文件，与地图任务零冲突）。
    /// 持有 WSClient / MessageDispatcher / AgentRegistry，把消息流接到角色系统。
    /// </summary>
    public class SimulationClient : MonoBehaviour
    {
        public string backendUrl = "ws://localhost:5001/ws";

        public static SimulationClient Instance { get; private set; }

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
```

### 3.2 `Assets/_Project/Scripts/Editor/AgentAssetGenerator.cs`（切片+Prefab，Editor only）

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZZR.Agents;

namespace ZZR.EditorTools
{
    /// <summary>
    /// 角色资产生成：把 Resources/Agents/<名>/texture.png 按 3列×4行 32px 网格切片
    /// （行序 down/left/right/up，列 0/1/2；站立帧=列1，走路序列由 AgentController 控制），
    /// 并生成通用 Agent.prefab（SpriteRenderer + AgentMovement + AgentController + TextMesh 名牌）。
    /// 菜单: MAVIS/Agents/Generate Assets —— 素材或 prefab 结构变更后重跑即可，幂等。
    /// </summary>
    public static class AgentAssetGenerator
    {
        const string AgentsRoot = "Assets/_Project/Resources/Agents";
        const string PrefabPath = "Assets/_Project/Resources/Agent.prefab";
        const int Tile = 32;
        static readonly string[] Rows = { "down", "left", "right", "up" };

        [MenuItem("MAVIS/Agents/Generate Assets")]
        public static void Generate()
        {
            AssetDatabase.Refresh();
            int sliced = 0;
            foreach (var dir in Directory.GetDirectories(AgentsRoot).OrderBy(d => d))
            {
                string texPath = Path.Combine(dir, "texture.png").Replace('\\', '/');
                if (!File.Exists(texPath)) continue;
                SliceTexture(texPath);
                sliced++;
            }
            if (sliced == 0) { Debug.LogError($"[AgentAssetGenerator] {AgentsRoot} 下没找到 texture.png"); return; }
            BuildPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentAssetGenerator] 完成: {sliced} 张贴图切片 + Agent.prefab");
        }

        static void SliceTexture(string texPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogError($"[AgentAssetGenerator] 无法加载: {texPath}"); return; }
            if (tex.width != Tile * 3 || tex.height != Tile * 4)
            {
                Debug.LogError($"[AgentAssetGenerator] 尺寸不符(期望 96×128): {texPath} 实际 {tex.width}×{tex.height}");
                return;
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.spritePixelsPerUnit = Tile;   // 1 格 = 1 unit，与 TiledMapLoader 一致
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 128;

            var metas = new List<SpriteMetaData>();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                    metas.Add(new SpriteMetaData
                    {
                        name = $"{Rows[row]}_{col}",
                        // Unity 切片 rect 原点在左下，texture 行 0(down) 在顶部 → y 翻转
                        rect = new Rect(col * Tile, tex.height - (row + 1) * Tile, Tile, Tile),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = (int)SpriteAlignment.Center,
                    });
            importer.spritesheet = metas.ToArray();
            importer.SaveAndReimport();
        }

        static void BuildPrefab()
        {
            var go = new GameObject("Agent");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 50;   // 地表 tilemap 之上、Foreground(+100) 之下
            go.AddComponent<AgentMovement>();
            go.AddComponent<AgentController>();

            var labelGo = new GameObject("Nameplate");
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28;
            label.characterSize = 0.13f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            label.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 60;

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }
    }
}
```

### 3.3 `tools/mock_ws_server.py`（回放器，放工程根 tools/ 下）

依赖：`pip install websockets`。

```python
"""Mock WS Server — 重放 provenance checkpoints 存档为 mavis WS 契约消息流。
用法:
  python tools/mock_ws_server.py                                # gtc-demo14, port 5001
  python tools/mock_ws_server.py --checkpoint gtc-demo12 --speed 2 --loop
依赖: pip install websockets
Unity 侧连接: ws://localhost:5001/ws (SimulationClient.backendUrl 默认值)
"""
import argparse, asyncio, json, os
import websockets

HERE = os.path.dirname(os.path.abspath(__file__))
PROV = os.path.normpath(os.path.join(HERE, "..", "..", "provenance", "provenance"))
CKPT = os.path.join(PROV, "results", "checkpoints")
STORY = os.path.join(PROV, "scenarios", "investment", "story.json")


def action_str(a):
    """存档里 action 是 {"event":{subject,predicate,object}}，live 消息是字符串，这里拼平。"""
    if isinstance(a, str):
        return a
    ev = (a or {}).get("event") or {}
    return " ".join(str(x) for x in (ev.get("subject"), ev.get("predicate"), ev.get("object")) if x)


def load_all(ckpt):
    d = os.path.join(CKPT, ckpt)
    files = sorted(f for f in os.listdir(d) if f.startswith("simulate-") and f.endswith(".json"))
    steps = [json.load(open(os.path.join(d, f), encoding="utf-8")) for f in files]
    conv = {}
    conv_path = os.path.join(d, "conversation.json")
    if os.path.exists(conv_path):
        conv = json.load(open(conv_path, encoding="utf-8"))
    story = {}
    if os.path.exists(STORY):
        for ev in json.load(open(STORY, encoding="utf-8")).get("events", []):
            story.setdefault(ev.get("time", ""), []).append(ev)
    return steps, conv, story


class Server:
    def __init__(self, steps, conv, story, interval, loop):
        self.steps, self.conv, self.story = steps, conv, story
        self.interval, self.loop = interval, loop
        self.clients = set()
        self.cursor = 0

    async def handler(self, ws):
        await ws.send(json.dumps({"type": "init"}, ensure_ascii=False))
        await self.send_snapshot(ws)
        self.clients.add(ws)
        try:
            async for _ in ws:  # 客户端不发消息，占住连接即可
                pass
        finally:
            self.clients.discard(ws)

    async def send_snapshot(self, ws):
        if not self.steps:
            return
        step = self.steps[min(self.cursor, len(self.steps) - 1)]
        agents = {
            name: {"type": "agent", "name": name, "coord": st.get("coord"),
                   "action": action_str(st.get("action")), "location": st.get("location", "")}
            for name, st in step.get("agents", {}).items()
        }
        await ws.send(json.dumps({"type": "snapshot", "time": step.get("time"), "agents": agents},
                                 ensure_ascii=False))

    async def run(self):
        while True:
            if self.cursor >= len(self.steps):
                if self.loop:
                    self.cursor = 0
                else:
                    await self.broadcast({"type": "done"})
                    await asyncio.sleep(60)
                    continue
            step = self.steps[self.cursor]
            t = step.get("time", "")
            msgs = [{"type": "time", "time": t}]
            for name, st in step.get("agents", {}).items():
                status = st.get("status") or {}
                msgs.append({
                    "type": "agent", "name": name,
                    "coord": st.get("coord"),
                    "action": action_str(st.get("action")),
                    "currently": st.get("currently", ""),
                    "role_type": "user",   # 存档无此字段，省略也行；AI 徽标只在真后端下准
                    "value_tendency": status.get("value_tendency") or {},
                    "goal_alignment": status.get("goal_alignment") or {},
                    "time": t,
                })
            for block in (self.conv.get(t) or []):
                if not isinstance(block, dict):
                    continue
                for lines in block.values():
                    if isinstance(lines, list):
                        for pair in lines:
                            if isinstance(pair, list) and len(pair) == 2:
                                msgs.append({"type": "chat_line", "speaker": pair[0], "text": pair[1]})
            for ev in self.story.get(t[9:] if len(t) >= 9 else t, []):
                msgs.append({"type": "story", "id": ev.get("id"), "time": t,
                             "event_type": ev.get("event_type"), "content": ev.get("content"),
                             "targets": ev.get("targets")})
            for m in msgs:
                await self.broadcast(m)
            self.cursor += 1
            await asyncio.sleep(self.interval)

    async def pinger(self):
        while True:
            await asyncio.sleep(5)
            await self.broadcast({"type": "ping"})

    async def broadcast(self, msg):
        data = json.dumps(msg, ensure_ascii=False)
        for ws in list(self.clients):
            try:
                await ws.send(data)
            except Exception:
                self.clients.discard(ws)


async def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--checkpoint", default="gtc-demo14")
    ap.add_argument("--port", type=int, default=5001)
    ap.add_argument("--speed", type=float, default=1.0)
    ap.add_argument("--interval", type=float, default=0, help="覆盖每步间隔秒(默认 stride/speed)")
    ap.add_argument("--loop", action="store_true", help="播完从头循环(默认发 done)")
    args = ap.parse_args()

    steps, conv, story = load_all(args.checkpoint)
    if not steps:
        raise SystemExit(f"checkpoint 无 simulate-*.json: {args.checkpoint}")
    stride = steps[0].get("stride", 2)
    interval = args.interval or stride / max(args.speed, 0.01)
    srv = Server(steps, conv, story, interval, args.loop)

    print(f"[mock] checkpoint={args.checkpoint} steps={len(steps)} stride={stride} interval={interval:.2f}s")
    print(f"[mock] {steps[0].get('time')} → {steps[-1].get('time')}")
    async with websockets.serve(srv.handler, "localhost", args.port):
        asyncio.ensure_future(srv.run())
        asyncio.ensure_future(srv.pinger())
        print(f"[mock] listening ws://localhost:{args.port}/ws")
        await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())
```

**无 Unity 自测**（先跑 server，再开另一终端跑客户端脚本，应看到 init/snapshot/time/agent×6/chat_line…）：

```bash
python tools/mock_ws_server.py --checkpoint gtc-demo14 --speed 2
# 另一终端:
python -c "
import asyncio, websockets, json
async def t():
    async with websockets.connect('ws://localhost:5001/ws') as ws:
        for i in range(20):
            m = json.loads(await ws.recv())
            print(m.get('type'), (m.get('name') or m.get('speaker') or m.get('time') or '')[:24])
asyncio.run(t())"
```

## 4. 验证流程（M1/M2，全用 MCP 驱动 Editor）

1. 装 NativeWebSocket（§2）→ `wait_for_compilation` 无错误。
2. 写入 §3 三个文件 → `editor_control` force_refresh → 编译无错误。
3. MCP `execute_menu_item`，参数 menuPath=`MAVIS/Agents/Generate Assets` → `read_console` 确认 "完成: 6 张贴图切片 + Agent.prefab"。
4. 起回放器（§3.3 命令）→ MCP `play_mode_control` play → `read_console` 看：`[AgentRegistry] 生成角色`×6（中文名→英文名映射生效）、`[Agent] AI投顾助手 → …`、`[Sim] 模拟时间 → …`。`screenshot` game_view 看 6 个角色在地图上走动、方向动画正确、名牌跟随。
5. 重连测试：停掉回放器 → 等 ~23s（20s 看门狗+3s 重连）console 出现判死日志 → 重启回放器 → Unity 自动重连，新连接收 init+snapshot，角色贴齐到当前进度位置（Console 有 "Snapshot 追赶"）。
6. **M1 达成**：以上全部通过 → git 提交（§5）。
7. **M2**（可选，需 LLM 环境）：起真后端（§1.4 命令），Play 后 Console 应看到英文名角色（`AI Advisor` 等）消息且 ai_tool 蓝字名牌；移动闭环同上。
8. 结束 Play 用 `play_mode_control` stop。

常见排错：编译错先看 `read_console` get_errors；角色贴图缺失=洋红方块=没跑生成菜单或 Resources 路径不对；角色生成但不动=消息没到（回放器窗口看 broadcast 日志）或 coord 解析失败；位置错乱=MapHeight 没同步（地图任务场景未加载时兜底 24，若仍不对检查 TiledMapLoader 是否在场景里）。

## 5. git 提交规则（用户明确要求：分阶段提交；**不要提交地图任务线的东西**）

仓库：工程根目录，分支 main（remote github，**只 commit 不 push**，用户没让 push）。

**属于本线、应提交的路径**：
```
.gitignore
Assets/StreamingAssets/agent_alias.json (+ .meta)
Assets/_Project/Scripts/Core/            （SimulationClient/MessageDispatcher/AgentRegistry + .meta）
Assets/_Project/Scripts/Network/         （WSClient + .meta）
Assets/_Project/Scripts/Agents/          （AgentController/AgentMovement + .meta）
Assets/_Project/Scripts/Data/WsMessages.cs、MapCoords.cs（+ .meta）
Assets/_Project/Resources/Agents/        （6 个 texture.png + .meta）
Assets/_Project/Art/Agents/              （portrait/agent.json/sprite.json + .meta）
Assets/_Project/Resources/Agent.prefab   （生成菜单产出后）
tools/mock_ws_server.py
Packages/manifest.json                   （加 NativeWebSocket 之后）
docs/HANDOFF_v0.1.md
```

**不要动（地图任务线，untracked 就放着）**：`Assets/Scenes/Demo.unity`、`Assets/StreamingAssets/Maps/`、`Assets/_Project/Art/Tilesets/`、`Assets/_Project/Scripts/Data/Tiled*.cs`、`Assets/_Project/Scripts/Editor/TiledTilesetManifestGenerator.cs`、`Assets/_Project/Resources/TiledTilesetManifest.asset`、`Packages/com.jlceaser.unity-mcp-vibe/`、`ProjectSettings/SceneTemplateSettings.json`、`docs/DEVELOPMENT_PLAN.md`（归地图线或用户）。

建议提交信息：
1. 装包+三个文件写完后：`feat(agents): v0.1 角色系统接入 provenance WS 契约(网络层/消息分发/动态注册/waypoint 移动/帧动画)`
2. M1 验证通过后：`feat(tools): 存档回放器 mock_ws_server + M1 联调通过(6 角色移动/断线重连)`

## 6. 坑与设计决策备忘（下一个 AI 必读）

1. **中英文角色名双轨**：旧存档中文、资产/新后端英文。AgentRegistry 靠 `agent_alias.json` 映射，映射不到按原名加载。长期解法=用英文名重跑一份存档。
2. **存档 path 恒为 null**：客户端已兼容（无 path → MoveTo(coord) 直线走）。若想要逐格路径，可在回放器加 A*（maze.json 有碰撞数据），v0.2 再说。
3. **帧动画故意不用 Animator**：AgentController 手动切 sprite（4 方向×[0,1,2,1]@10fps，站立=列1）。原因：12 帧小网格上状态机纯属负担，且与 Phaser 行为逐帧对齐。要升级时写 Editor 生成 AnimatorController 替换即可，AgentMovement 的 SetLocomotion 接口不变。
4. **名牌用 TextMesh（内置 LegacyRuntime.ttf）**：v0.1 只显示英文名。中文（action 文本、chat）必须等 NotoSansSC SDF 字体打包（DEVELOPMENT_PLAN Phase 0 项），WebGL 没有系统字体 fallback。
5. **排序层**：地表 tilemap 0..N、地图任务 Foreground 是 +100；agent SpriteRenderer=50、名牌 MeshRenderer=60，天然夹在中间。
6. **速度**：1.5 格/s 恒速（Phaser live 模式 48px/s），高刷新屏不会加速（按 deltaTime 折算）。走路动画 10fps。
7. **重连语义**：判死→3s 重连→新连接后端必发 init(+snapshot)，客户端 ApplySnapshot 贴齐。**移动中掉线的旧队列会被 SnapTo 清掉**，不会累计漂移；>3 格偏差的路径点也会被贴齐（Phaser 的防穿墙保险）。
8. **倾向数据已就绪但没 UI**：`MessageDispatcher.TendencyHistory[name]` = List<{time,tendency}>（去重、cap 200），后续 TendencyChart 直接消费；约束期望（虚线基准）读 `StreamingAssets`（v0.2 从 governance.json 拷入）。
9. **WebGL 铁律**：不用 System.Net.WebSockets（WSClient 用 NativeWebSocket）；无线程（收包靠 Update 里 Pump）；后端地址 iframe 参数 `?backend=ws://...`（ResolveUrl 已实现）。
10. **地图接口**：只用 `TiledMapLoader.Map.height`（SyncMapHeight）与坐标系约定；不依赖 LogicalCells/碰撞层（移动不查碰撞，路径来自后端寻路）。地图没加载也不崩（MapCoords 兜底 24）。

## 7. v0.1 之后（按 DEVELOPMENT_PLAN.md 排期，非本次范围）

chat 气泡（chat_line→打字机）、时钟/事件横幅、TendencyLabel/TendencyChart（评委核心）、治理滑条（POST /api/goals，400ms debounce）、NotoSansSC、WebGL 打包与仝牧平台 iframe 联调（postMessage 选角联动）。
