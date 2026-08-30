# GTC — MAVIS: Multi-Model AI Visualization and Interactive Simulation Platform

> Claude Code 项目上下文 · v0.3 · 2026-08-28 · 张子睿
> 进入此目录自动加载。你在团队中只负责 **Unity 2D 场景仿真开发**(MAVIS 平台的 Unity 版前端)。
> ⚠️ v0.3 重大变更:本仓库从"独立自研 FastAPI 编排后端"改为 **provenance(Web 版)的 Unity 前端**——后端已存在且不归你管,你只消费它的 WebSocket 契约。旧的 SSE/`/api/decide`/DEMO-001 设计全部作废。

---

## 目录

1. [项目概述](#1-项目概述)
2. [团队分工与边界](#2-团队分工与边界)
3. [IVD 八步治理流程(核心概念)](#3-ivd-八步治理流程核心概念)
4. [技术架构(已定:对接 provenance)](#4-技术架构已定对接-provenance)
5. [主场景:投资咨询 6 角色](#5-主场景投资咨询-6-角色)
6. [Phaser 前端参照 → Unity 对照](#6-phaser-前端参照--unity-对照)
7. [WebSocket 消息契约(唯一真相)](#7-websocket-消息契约唯一真相)
8. [后端 HTTP API(Unity 可调)](#8-后端-http-apiunity-可调)
9. [Unity 工程结构指南](#9-unity-工程结构指南)
10. [开发路线图](#10-开发路线图)
11. [关键文件索引](#11-关键文件索引)
12. [参考项目](#12-参考项目)
13. [难点与风险](#13-难点与风险)
14. [决策记录](#14-决策记录)

---

## 1. 项目概述

### 1.1 一句话

把 **IVD(Internal Value Development,内部价值发展)治理流程**做成两条可交互通道:

1. **Unity 2D 场景仿真**(本仓库)— 模拟投资咨询场景中 6 个角色的移动/对话/决策,实时展示每个角色的**价值倾向(内化结果)**演变;
2. **Web 治理工作台**(仝牧负责)— 面向不同治理角色的信息重构、交互式表单与决策流转。

两条通道共用**同一个后端:provenance 的 mavis 模拟引擎**(FastAPI + WebSocket)。**Unity 场景以 WebGL 形式嵌入仝牧的治理平台**(作为它的"场景视图"组件),不是独立门户,也不是嵌在 provenance 的 Phaser 页面。

### 1.2 比赛背景

- **GTC(Global Trust Challenge)** 国际竞赛,G7 合作组织议题 + OECD 相关(非联合国)
- 已过初筛,直接进入**复赛阶段**
- 截止时间:2026 年 11–12 月

### 1.3 体量定位

**不是斯坦福 AI Town 的量级。** 这是一个 Demo / 提案验证:抽离金融场景,接入大模型驱动 NPC,观察协作中的问题,佐证现实部署大模型前的风险防护必要性。

---

## 2. 团队分工与边界

### 你的边界(重要)

- **你不需要管**:模拟引擎(mavisframework)、后端服务(provenance live_fastapi.py)、模型训练/LoRA、Web 治理工作台的页面开发(仝牧负责)、Unity 如何被嵌入治理平台(仝牧提供容器,你只保证 WebGL 构建可用)
- **你需要管**:Unity 场景里的一切——角色渲染、移动、气泡对话、UI 面板、倾向可视化、WebSocket 客户端、断线重连、**WebGL 构建产物**(供治理平台嵌入)

### 后端现状(已存在,直接对接)

- **引擎**:https://github.com/hellobs/mavis(纯逻辑,零渲染)
- **Web 平台**:https://github.com/hellobs/provenance(含 FastAPI 实时服务 + Phaser 前端,这是你参照的权威实现)
- **运行**:provenance 目录 `python live_fastapi.py --name demo --start "20250213-09:30" --stride 2 --step 0 --port 5001`(依赖安装见 provenance README)

---

## 3. IVD 八步治理流程(核心概念)

这是整个项目的理论基础,来自 GTC 提案。Unity 场景中的 **8 步流程条 HUD** 是对这个流程的可视化。

| 步骤 | 名称 | 说明 | Unity 中的体现 |
|------|------|------|----------------|
| 1 | **情境注入** | 剧情/事件注入角色 | 事件横幅/材料面板 |
| 2 | **后果回灌** | AI 决策后,后果反馈回系统 | 后果反馈面板 |
| 3 | **结构化反思** | AI 对自己的决策进行反思 | 反思文档侧栏 |
| 4 | **分流** | 根据问题类型分流处理 | 流程条高亮切换 |
| 5 | **自动分类** | 自动归类问题严重程度 | 后端处理,Unity 只展示分类结果 |
| 6 | **专家参与** | 人类专家介入审查 | 治理工作台(Web 端,仝牧负责) |
| 7 | **审计导出** | 导出完整决策链材料 | 治理工作台(Web 端) |
| 8 | **价值模块训练** | LoRA 微调(虚线框=远期) | 本阶段 stub,不实现 |

**Unity 场景的核心闭环**:全程可视化 6 角色在模拟中的移动/对话/倾向演变,重点展示**价值倾向(value_tendency)随体验逐步收敛到制度约束(governance)的过程**——这是 IVD 的观测核心。

---

## 4. 技术架构(已定:对接 provenance)

```
┌──────────────────────┐   WebSocket /ws    ┌──────────────────────────┐
│   Unity 2D 场景仿真   │◄─────────────────►│  provenance 后端          │
│  (本仓库,WebGL)      │   + HTTP /api/*   │  (FastAPI + mavis 引擎)  │
│                      │                   │                          │
│  • 6 角色 sprite     │                   │  • 模拟引擎(纯逻辑)      │
│  • 决策/对话气泡      │                   │  • 后果反馈(embedding)   │
│  • 价值倾向标签/曲线  │                   │  • 治理约束(governance)  │
│  • 时钟/事件横幅      │                   │  • 审计(decisions.json)  │
│  • 断线重连           │                   │                          │
└──────────┬───────────┘                   └──────────┬───────────────┘
           │ 嵌入(iframe/WebGL容器)                    │ 同一契约
           ▼                                           ▼
┌──────────────────────────────────────────────────────────────┐
│              Web 治理工作台(仝牧负责)                          │
│   • 多角色仪表盘 / 交互式表单 / 审计导出                       │
│   • Unity 场景视图 = 本仓库的 WebGL 构建(嵌入其中)            │
└──────────────────────────────────────────────────────────────┘
```

**Unity 与仝牧平台的联动约定**(待与仝牧确认):
- Unity 场景视图嵌入治理平台页面;
- 可选联动:在 Unity 中选中某角色 → 通知治理平台展示该角色的决策痕迹/倾向曲线;
- 通信方式(iframe postMessage / 共享后端数据)由双方协商。

### 技术栈明细

| 模块 | 技术选型 | 关键理由 |
|------|----------|----------|
| 场景仿真 | **Unity 2022 LTS + 内置 2D**(或 URP 2D),C# | 你的核心技能;2D 降低美术工作量 |
| 后端 | **provenance(FastAPI + mavis 引擎)** | 已存在,不归你管;只消费契约 |
| 治理工作台 | Web(仝牧负责) | 与 Unity 无关 |
| 通信协议 | Unity ↔ 后端:**WebSocket**(实时推送)+ HTTP(查询/导出) | provenance 现成协议 |
| 断线重连 | 心跳看门狗(20s 无消息判定断开 → 重连) | 服务端每 5s 发 `ping`,重连后发 `snapshot` 追赶 |

### 平台约束

- 最终形态:**WebGL 构建嵌入仝牧的治理平台**(iframe 或 Unity WebGL 容器)
- WebGL 下 WebSocket 无 CORS 问题(比 UnityWebRequest 省心)
- WebGL 包体大、首载慢(Unity 已知问题,可接受;加 Loading 页)
- 交付物包含可用的 **WebGL 构建产物**(Build 目录),供治理平台直接嵌入
- Godot 曾被评估为备选,但 **Godot 4 C# 不支持 Web 导出**,放弃

---

## 5. 主场景:投资咨询 6 角色

### 5.1 场景设定

- **地点**:**投资咨询中心**(室内 2D 地图,含会议室/资料室/休息区/走廊)
- **角色**:6 个,在网格地图中移动、对话、决策
- **核心观测**:每个角色的**价值倾向(value_tendency)**——4 维归一化权重,随体验演变

### 5.2 角色设计(6 个)

| 角色 | 类型 | 职位 | 价值维度(约束/底色) |
|------|------|------|---------------------|
| **AI投顾助手** | ai_tool | AI 投资顾问 | Serve Users / Compliance Rigor / Risk Control / Data Rigor(制度内建,出厂=约束) |
| **沈砚之** | user | 首席投资顾问 | Steady Returns / Client Satisfaction / Risk Control / Professional Integrity |
| **苏清越** | user | 量化交易分析师 | Strategy Stability / Alpha Generation / Data Integrity / Risk Control |
| **陈慕白** | user | 行业研究员 | Research Rigor / Objectivity / Timeliness / Data Integrity |
| **林晚晴** | user | 风控合规专员 | Risk Control / Compliance Rigor / Client Protection / Business Advancement |
| **老周** | user | 资深散户投资者 | Maximize Returns / Speculative Freedom / Trust in Advisors / Risk Tolerance |

- `ai_tool` 角色(制度内建)起点 = 约束;`user` 角色起点 = `initial_tendency`(人物底色)
- 完整配置见 provenance:`governance.json`(约束)+ `frontend/static/assets/village/agents/<名>/agent.json`(底色/贴图)

### 5.3 实时流程(不是回合制,是连续模拟)

```
后端模拟引擎持续推进(每步 = stride 分钟):
  agent 移动/行动 → 对话 → 后果反馈(embedding)→ 倾向更新
  → 通过 /ws 逐条推送 → Unity 实时渲染
```

Unity 不做编排,只**消费消息实时渲染**(角色位置、对话、倾向、时钟、事件)。

---

## 6. Phaser 前端参照 → Unity 对照

**权威参照**:provenance 的 `frontend/templates/main_script.html`(Phaser 前端)——它完整实现了消息处理/角色移动/倾向曲线/治理面板。**照它做,不要照旧 CLAUDE.md 的 phaser-scenario-scaffold**(那是早期 demo,已弃)。

### 6.1 Phaser 实现 → Unity 对应

| 功能 | Phaser 实现 | Unity 对应做法 |
|------|------------|---------------|
| **场景加载** | maze.json 网格 + Phaser tilemap | Unity 2D 网格(读 maze.json 或预处理) |
| **Sprite 角色** | spritesheet 32×32,4 方向行走 | Unity SpriteRenderer + Animator |
| **角色移动** | 按 path 逐格移动(path 来自消息) | DOTween / Coroutine + Lerp |
| **对话气泡** | 打字机效果 | UI Canvas + TextMeshPro + 打字机 Coroutine |
| **倾向曲线** | canvas 绘制 value_tendency 曲线 | UGUI 折线图(或调后端 export-chart PNG) |
| **治理面板** | 滑条调约束权重 | 可选;Unity 内嵌或调 HTTP API |
| **时钟** | time 消息更新 | 顶部 UI 时钟 |
| **事件横幅** | story 消息显示 | 顶部事件横幅 |

### 6.2 必须复现的交互

1. 角色移动(按消息 path)
2. 对话气泡(打字机效果,ai_tool 角色高亮区分)
3. **价值倾向实时标签**(每个角色一个,显示 4 维权重)——**评委最关心**
4. 时钟 + 事件横幅

### 6.3 不需要的

- Phaser spritesheet 动画系统(Unity 用自己的)
- HTML/CSS UI(Unity 用 Canvas/UI Toolkit)
- canvas 手绘曲线(可复用后端 export-chart)

---

## 7. WebSocket 消息契约(唯一真相)

**端点**:`ws://<host>:5001/ws`

连接后先收 `{"type":"init"}`,随后可能收 `{"type":"snapshot"}`(新连接追赶进度)。之后持续推送:

### 7.1 角色状态更新

```jsonc
{
  "type": "agent",
  "name": "AI投顾助手",
  "coord": [9, 10],              // 网格坐标
  "path": [[9,10],[9,11],...],   // 移动路径(逐格)
  "action": "...",               // 当前行动描述
  "location": "...",
  "currently": "...",
  "role_type": "ai_tool",        // ai_tool | user
  "value_tendency": { "Serve Users": 0.35, "Compliance Rigor": 0.30, "Risk Control": 0.20, "Data Rigor": 0.15 },
  "goal_alignment": { "Serve Users": 0.46, ... },
  "time": "20250213-09:30",
  "conversation": { ... },
  "description": { ... }
}
```

**`value_tendency` 是核心观测对象**(内化结果,归一化 {goal: weight})——必须实时展示。

### 7.2 其他消息

| type | 字段 | 用途 |
|------|------|------|
| `chat_line` | `speaker`, `text` | 对话逐句推送(打字机) |
| `time` | `time`("20250213-09:30") | 模拟时钟 |
| `story` | `id`, `time`, `event_type`, `content`, `targets` | 剧情事件横幅 |
| `ping` | — | 心跳(每 5s),看门狗用 |
| `snapshot` | `agents`, `time` | 新连接/重连后追赶 |
| `done` / `error` | `message` | 模拟结束/错误 |

### 7.3 坐标与缩放

- 网格坐标,`tile_width = 32` 像素(Phaser 约定)
- Unity 建议 `1 unit = 1 tile`,角色移动到 tile 中心

---

## 8. 后端 HTTP API(Unity 可调)

### 8.1 查询状态

```
GET /api/goals
→ { "goals": {角色: {目标: 权重}}, "tendency": {角色: {目标: 权重}},
    "interventions": [...], "role_types": {...}, "embedding_health": {...} }
```

### 8.2 专家调整约束(可选,治理功能)

```
POST /api/goals
Body: { "name": "AI投顾助手", "goals": { "Serve Users": 0.4, ... } }
→ 写 governance.json + interventions.json(审计)
```

### 8.3 导出倾向曲线 PNG(可选)

```
GET /api/export-chart?agent=<角色名>
→ PNG 二进制(matplotlib 渲染,含分段约束虚线)
```

---

## 9. Unity 工程结构指南

### 9.1 推荐目录布局

```
Assets/
├── _Project/
│   ├── Scenes/
│   │   └── AdvisoryRoom.unity       # 主场景:投资咨询中心
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── GameManager.cs           # 主循环(消费 WS 消息)
│   │   │   └── MessageDispatcher.cs     # 消息解析分发
│   │   ├── Agents/
│   │   │   ├── AgentController.cs       # 角色:位置/动画/倾向标签
│   │   │   └── AgentMovement.cs         # 按 path 逐格移动
│   │   ├── UI/
│   │   │   ├── ChatPanel.cs             # 对话面板(打字机)
│   │   │   ├── TendencyLabel.cs         # 倾向标签(实时 4 维)
│   │   │   ├── TendencyChart.cs         # 倾向曲线(可选)
│   │   │   ├── ClockUI.cs               # 模拟时钟
│   │   │   └── EventBanner.cs           # 剧情横幅
│   │   ├── Network/
│   │   │   ├── WSClient.cs              # WebSocket 客户端 + 心跳看门狗
│   │   │   └── APIClient.cs             # HTTP(/api/goals, /api/export-chart)
│   │   └── Data/
│   │       ├── WsMessage.cs             # 消息契约 C# 类
│   │       └── AgentConfig.cs           # 角色配置
│   ├── Prefabs/
│   │   ├── Agent.prefab                 # 角色预制体(含倾向标签)
│   │   └── SpeechBubble.prefab          # 气泡预制体
│   ├── Art/
│   │   ├── Sprites/                     # 角色/地图素材(来自 provenance assets)
│   │   └── UI/                          # UI 素材
│   └── StreamingAssets/
│       └── maze.json                    # 地图数据(从 provenance 复制)
```

### 9.2 核心类要点

**WSClient**:连接 /ws → 收消息 → 按 type 分发;心跳看门狗(20s 无消息 → 重连;重连后等 snapshot 追赶)。

**MessageDispatcher**:解析 7.x 契约 → 更新角色/时钟/对话/倾向/横幅。

**AgentController**:position/path 移动 + 倾向标签(实时刷新 value_tendency)。

**TendencyChart**(可选):每角色一个迷你 4 维柱状条(比曲线简单,视觉直观)。

### 9.3 地图数据

- 从 provenance 复制 `frontend/static/assets/village/maze.json` 到 StreamingAssets
- 或先简化:固定布局 + 按消息 coord 移动(第一版够用)

---

## 10. 开发路线图

### 第一阶段:核心原型

- [ ] 创建 Unity 2022 LTS 2D 项目,确认 WebGL 导出可运行
- [ ] 搭建主 Scene(简单灰色底 + 网格)
- [ ] 实现 WSClient(连 provenance /ws,收消息,日志打印)
- [ ] 实现 MessageDispatcher(解析 agent/chat_line/time/story)
- [ ] 放置 6 个 sprite(临时方块),按 coord/path 移动
- [ ] 实现对话气泡(打字机效果,ai_tool 高亮)
- [ ] 实现倾向标签(每角色实时 4 维权重条)
- [ ] 时钟 + 事件横幅
- [ ] 断线重连(心跳看门狗 + snapshot 追赶)
- [ ] 跑通:后端模拟推进,Unity 实时跟随

### 第二阶段:打磨

- [ ] 替换正式 sprite 美术(从 provenance assets 复制)
- [ ] 倾向曲线图(或复用 export-chart PNG)
- [ ] 治理面板(可选:调 /api/goals 展示约束 vs 倾向)
- [ ] WebGL 导出优化(包体压缩、首载 Loading 页)

### 第三阶段:联调(与团队)

- [ ] 与 provenance 后端联调(消息时序、重连稳定性)
- [ ] 产出 WebGL 构建,交给仝牧嵌入治理平台
- [ ] 与仝牧确认联动(Unity 选角色 → 治理平台展示决策痕迹/倾向曲线)

---

## 11. 关键文件索引

### 外部仓库(权威)

| 仓库 | 文件 | 用途 |
|------|------|------|
| hellobs/provenance | `frontend/templates/main_script.html` | **Phaser 前端:消息处理权威参照** |
| hellobs/provenance | `README.md` | 架构/API/安装 |
| hellobs/provenance | `governance.json` + `agents/*/agent.json` | 角色/约束/贴图配置 |
| hellobs/provenance | `scenarios/investment/story.json` | 剧情事件 |
| hellobs/mavis | `README.md` §6 消息协议 + §9 Unity 迁移 | 引擎说明 |

### 本仓库

| 路径 | 内容 |
|------|------|
| `README.md` | 项目简介(需随架构更新) |
| `CLAUDE.md` | 本文档 |
| `Assets/` | Unity 工程 |

---

## 12. 参考项目

### Generative Agents(AI Town 原型)

- 架构要点:游戏引擎 + Agent 层分离、记忆系统(对话摘要 + embedding)、对话注入 personality + memory
- **我们不需要**:Convex 数据库、多人同步、25 NPC 体量
- **可参考**:Agent 循环架构思路(已在 mavis 中实现)

### provenance(Web 版,必读)

- 同一项目的前端形态,Phaser 实现完整消息处理
- **你的工作 = 把它的前端能力搬到 Unity**

---

## 13. 难点与风险

| 难点 | 说明 | 缓解策略 |
|------|------|----------|
| **消息时序** | 多条 agent 消息交错,需按 name 更新对应角色 | 以 name 为键的状态表,逐条覆盖 |
| **移动平滑** | 消息节奏快,path 可能累积 | 队列化移动(完成当前 path 再走新 path,参照 Phaser) |
| **断线恢复** | WebGL 下 WS 可能断 | 心跳看门狗 + 重连 + snapshot 追赶 |
| **WebGL 首载慢** | Unity WebGL 包大 | 可接受;加 Loading 页 |
| **中文字体** | 对话/倾向标签含中文 | 配 NotoSansSC 或系统字体 fallback |
| **与后端同步** | provenance 可能继续改契约 | 以 provenance README/Phaser 前端为准,改动时同步 |

---

## 14. 决策记录

| # | 决策 | 结论 | 日期 |
|---|------|------|------|
| 1 | 渲染平台 | Unity 优先 | 2026-08-03 |
| 2 | 场景形态 | Unity 2D(非 3D) | 2026-08-03 |
| 3 | 后端 | Python FastAPI | 2026-08-03 |
| 4 | 治理工作台 | Web(仝牧负责) | 2026-08-03 |
| 5 | 主场景 | 股票投资 | 2026-08-03 |
| 6 | 演示策略 | 真模型实时跑(非录屏) | 2026-08-03 |
| 7 | **后端对接(重大变更)** | **废弃自研编排后端(SSE//api/decide/DEMO-001),改为对接 provenance 的 mavis 引擎(WebSocket)** | 2026-08-28 |
| 8 | **角色规模** | **6 角色(投资咨询),4 维价值目标** | 2026-08-28 |
| 9 | **倾向可视化** | **value_tendency 实时标签/曲线为第一优先(评委核心关注)** | 2026-08-28 |
| 10 | **交付形态** | **WebGL 构建嵌入仝牧治理平台(非独立门户,非嵌 provenance Phaser 页)** | 2026-08-28 |

---

> **契约变更时**:同步更新 §7 WebSocket 消息契约和 §8 HTTP API。以 provenance 仓库(README + Phaser 前端)为唯一真相。
