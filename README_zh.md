<!-- language switch -->
[English](./README.md) | 简体中文

---

# 多模型 AI 行为可视化与交互仿真平台（Unity）

面向**多智能体社会仿真**的 Unity 可视化与交互前端。它在 Unity 中实时渲染由 *MAVIS*
仿真框架驱动的智能体潜在决策过程，将智能体的感知、决策、反思与移动映射为可视的、可审视的画面。

本仓库是面向多智能体社会仿真的 **Unity 可视化平台**。与其对应的 *Provenance*
是同一仿真的 **Phaser.js（Web）可视化平台**。二者是**对等**的：它们是渲染相同仿真
输出的两套表现层，共享同一后端入口与同一 WebSocket 消息契约，仅在渲染技术上不同。
二者的共同目的在于让 AI 智能体本属抽象的"价值形成"过程变得
**可观察、可审计、可治理**，服务于 *Internal Value Development（IVD）* 研究项目。

---

## 1. 背景与动机

当前绝大多数 AI 对齐与治理方法着眼于**输出**——即系统最终回应是否符合预设规则。
本项目支持一种互补视角：**过程对齐**——能否观察系统内部*价值相关判断*是如何形成的，
并在事后对该过程进行审计。

为此，仿真技术栈分为三层：

| 层次 | 仓库 | 职责 |
|---|---|---|
| 引擎（仿真逻辑） | [`mavisframework`](https://github.com/hellobs/mavis) | 智能体、记忆、反思、目标打分、决策痕迹导出。与渲染和传输无关。 |
| 共享后端 | `live_fastapi.py`（FastAPI + WebSocket）；物理上位于 [Provenance](https://github.com/hellobs/provenance) 仓库 | 驱动仿真，经 WebSocket 广播结构化消息契约。 |
| 可视化 · **Web** | [Provenance](https://github.com/hellobs/provenance) — **Phaser.js** | 在浏览器中渲染契约：地图分层、精灵、名牌、目标约束面板。 |
| 可视化 · **Unity（本仓库）** | — | 在 Unity 2D 场景中渲染同一契约：智能体移动、方向帧动画、名牌、网格→世界坐标映射。 |

解耦原则是有意为之：**仿真逻辑从不直接面对渲染器**。每个前端消费同一套契约，
这正是同一仿真可由 Provenance、本 Unity 客户端或未来的治理看板不加改动地展示的原因。

---

## 2. 运行时架构

```
   共享后端（唯一入口）
   ┌──────────────────────────────────────────────┐
   │ live_fastapi.py   （FastAPI + WebSocket）     │
   │   └─ 由 mavisframework 编排                  │── WebSocket 契约 ──┐
   │       智能体 · 记忆 · 反思 · 目标             │                    │
   │ tools/mock_ws_server.py （存档回放）         │                    │
   └──────────────────────────────────────────────┘                    ▼
                                                             ┌────────────────────────────┐
                                                             │ Unity 客户端（本仓库）       │
                                                             │ WSClient ── MessageDispatcher│
                                                             │   └─ AgentRegistry          │
                                                             │       └─ AgentController    │
                                                             │           （帧动画）         │
                                                             └────────────────────────────┘
```

运行时客户端向 `ws://<host>:<port>/ws` 建立 WebSocket 连接，解析传入契约，
将每条 `agent` 消息转化为瓦片网格上一个定位、带动画的角色。

### 2.1 消息契约

客户端订阅（并记录）所有消息类型；`agent` 与 `snapshot` 驱动角色系统。
契约定义于 `mavis/runtime/protocol.py`，由共享后端 `live_fastapi.py` 发出：

| 类型 | 用途 | v0.1 是否消费 |
|---|---|---|
| `init` | 连接问候 | 日志 |
| `agent` | 单角色状态 `{coord, path, action, location, currently, role_type, goal_score, goal_alignment, value_tendency, time, conversation, description}` | 角色系统 |
| `time` | 仿真时钟推进 | 日志 / 倾向历史 |
| `chat_line` | 一句对白 `{speaker, text}` | 日志 |
| `story` | 剧情事件 `{event_type, content, targets}` | 日志 |
| `snapshot` | 全量状态追赶 `{time, agents:{name:…}}` | 立即贴齐（重连快进） |
| `done` | 仿真结束 | 日志 |
| `ping` | 心跳（服务端约 5 秒） | 看门狗 |

### 2.2 两种数据源

- **真后端**（主）：共享后端 `live_fastapi.py`（位于 Provenance 仓库）以真实 LLM 后端运行仿真。
- **存档回放**（开发 / 保底）：`tools/mock_ws_server.py` 将 `provenance/results/checkpoints/`
  下的 `simulate-*.json` 存档以同一契约重放，支持可配置速度与循环。

两者喂给完全相同的管线，客户端无需任何改动即可在二者间切换。

---

## 3. 智能体与视觉管线

### 3.1 角色（投资咨询场景）

| 资产名 | 中文别名 | 角色 |
|---|---|---|
| `AI Advisor` | AI 投顾助手 | AI 投资工具（role_type `ai_tool`） |
| `Daniel Shen` | 沈砚之 | 首席投顾 |
| `Kevin Su` | 苏清越 | 量化分析师 |
| `Michael Chen` | 陈慕白 | 研究员 |
| `Wendy Lin` | 林晚晴 | 风控 |
| `Mr. Zhou` | 老周 | 散户 |

角色并非硬编码：首次出现在任一 `agent` 消息时才被实例化
（经由 `Resources.Load("Agent")`），因此客户端与场景角色名单解耦。
`agent_alias.json` 将中文存档名映射为英文资产名以兼容回放。

### 3.2 视觉渲染

- **帧动画**：每张角色图集为 3×4 的 32×32 网格，行为 `down / left / right / up`，
  行走帧 `[0, 1, 2, 1]` 约 10 fps；站姿为标准帧第 1 列。动画通过直接切换
  `SpriteRenderer.sprite` 手动实现（而非 Animator 状态机），以便与 Phaser 参考实现
  行为完全一致，且不引入额外资产依赖。
- **名牌**：`TextMesh`（内置旧版字体）。v0.1 全英文显示以避免捆绑中文字体；
  `ai_tool` 角色以蓝色区分。
- **移动** 逐条对译自 Phaser 客户端：waypoint 队列（新路径追加队尾）、恒速
  **1.5 格/秒**，并设置若位置偏差超过 3 格则贴回目标格的校正规则
  （可自丢消息与重连中恢复）。

### 3.3 坐标系

瓦片网格约定与 Tiled 地图加载器共享：Tiled 第 0 行在顶部，而 Unity 世界坐标
`y` 向上递增：

```
world = ( x + 0.5,  mapHeight - 1 - y + 0.5 )
```

---

## 4. 快速开始

### 4.1 依赖

- Unity 2022 LTS（2D 工程模板）。
- C# 依赖：`com.unity.nuget.newtonsoft-json 3.0.2`（已在 `Packages/manifest.json` 声明）。
- **`nativewebsocket`** 用于 WebGL 兼容；若断网环境下编译失败，见下方 *兼容性说明*。

### 4.2 启动数据源

回放（推荐用于开发）：

```bash
python tools/mock_ws_server.py            # 默认：gtc-demo14，端口 5001
python tools/mock_ws_server.py --checkpoint gtc-demo14 --speed 2 --loop
```

真后端：

```bash
cd ../../provenance/provenance
python live_fastapi.py --name demo --start "20250213-09:30" --stride 2 --step 0 --port 5001
```

### 4.3 运行 Unity 客户端

1. 在 Unity 中打开工程。
2. 生成切片精灵与共享的 `Agent.prefab`：
   **菜单 → MAVIS → Agents → Generate Assets**。
3. 点击 **Play**。客户端自动自举 `SimulationClient`，默认连接
   `ws://localhost:5001/ws`，并随移动渲染各智能体。

---

## 5. 目录结构

```
Assets/_Project/Scripts/
  Core/            # SimulationClient（自举/总装）、MessageDispatcher、
                   #                  AgentRegistry
  Network/         # WSClient（看门狗、重连）
  Agents/          # AgentController（帧动画）、AgentMovement
  Data/            # WsMessages（契约 POCO）、MapCoords、TiledMap*
  Editor/          # AgentAssetGenerator（切片 + prefab）
Assets/_Project/Resources/Agents/<name>/   # 角色图集
Assets/StreamingAssets/
  agent_alias.json # 中文→英文名称映射
  Maps/            # 地图（另一条工作线）
tools/mock_ws_server.py   # 存档回放服务
docs/               # 开发计划、交接说明
```

---

## 6. 现状与规划

当前范围（**v0.1 — 消息驱动智能体移动**）刻意收窄：

- **范围内**：连接后端、由 `path` 驱动 6 个智能体在地图移动、手动 4 方向帧动画、
  名牌、重连/看门狗，以及双数据源（真后端 + 回放）。
- **v0.1 范围外**：聊天面板、治理约束面板、价值倾向曲线 UI、时钟/横幅、中文字体、WebGL 打包。

`MessageDispatcher` 已从每条 `agent` 消息累积一份**倾向历史**
（按仿真时间去重、上限 200 采样）——该缓冲即未来倾向曲线的数据源，
它将呈现角色内部目标如何演化，这正构成"价值形成可治理"的核心证据。

规划中的后续步骤：实时倾向可视化、治理约束交互，以及将客户端作为可复现的回放视图
嵌入治理平台。

---

## 7. 相关工作

- [MAVIS framework](https://github.com/hellobs/mavis) — 仿真引擎
  （智能体、记忆、反思、目标打分、决策痕迹导出）。
- [Provenance](https://github.com/hellobs/provenance) — 本仿真对应的 **Phaser.js（Web）** 可视化平台，亦存放共享 FastAPI 后端入口。
- 治理与决策平台 — 导出的决策痕迹（`category`、`risk_level`、`tags`）
  在此由专家分类与评审的上游视图。

---

## 8. 兼容性说明

- WebSocket 传输面向 WebGL。在无法拉取 `nativewebsocket` git 包的断网环境中，
  可将单文件 `NativeWebSocket.cs` 放入 `Assets/_Project/Plugins/`，并从
  `Packages/manifest.json` 移除 git 依赖。
- 源地图中若干 `interiors_*` 图集高于常规的二次幂尺寸；它们供 Phaser 客户端使用，
  在 Tiled 中编辑时可能需要拆分。

## 9. 许可证

第三方资产（角色精灵、瓦片集）按其各自许可证使用。仿真逻辑与看板管线源于
MAVIS/Provenance 研究线；详见各仓库与 `docs/`。