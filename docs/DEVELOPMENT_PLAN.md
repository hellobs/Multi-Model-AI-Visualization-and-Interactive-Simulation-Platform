# MAVIS Unity 平台 · 详细开发计划

> 配套阅读：`CLAUDE.md`（工程权威上下文 v0.3）。本计划由 2026-08-28 会话产出，作为执行基准；
> 完成一项就勾一项，状态变化请同步更新本文件。

## 一、目标与交付物

把 provenance 的 Phaser 前端能力完整搬到 Unity：实时渲染 6 角色的移动/对话/时钟/事件，并把 **value_tendency 实时可视化**做到位。最终交付三个东西：

1. Unity 工程（2022.3.62f3c1，2D，C#）
2. **WebGL 构建产物**（Build 目录 + 可嵌入 iframe 的页面）→ 交给仝牧嵌进治理平台
3. 一份对接说明（WebSocket 契约版本、嵌入方式、加载页要求），供仝牧侧接入

不做的事（边界）：模拟引擎、后端服务、治理工作台页面、LoRA 训练都不归这条线管。

## 二、总体策略（三条原则）

1. **Phaser 前端是唯一权威参照**：`provenance/provenance/frontend/templates/main_script.html` 已实现全部消息处理逻辑（移动队列、打字机、看门狗），照它的行为逐条翻译成 C#，不自创新协议。
2. **先脱离真后端开发**：第一步就做一个**回放器（Mock WS Server）**——用 Python 起一个本地 WS 服务，重放 `provenance/results/` 里的历史消息流。开发、回归测试都不依赖真模型跑；联调时再切真后端；答辩现场若 LLM 不稳，回放器兼做演示保底。
3. **WebGL 约束前置**：所有代码从第一天就按 WebGL 兼容写——不用 `System.Net.WebSockets`（WebGL 不支持），用 NativeWebSocket；不用多线程（WebGL 无线程），WS 收包靠主线程轮询；中文用打包进构建的 NotoSansSC 字体（WebGL 里没有系统字体 fallback）。

## 三、阶段分解

### Phase 0：工程准备（约 2~3 天）

- [ ] 打开工程确认 Unity 2022.3.62f3c1 能编译、能出 WebGL 构建空包（先趟一遍导出流程）
- [ ] 建 `Assets/_Project/` 目录骨架（按 CLAUDE.md §9.1：Scenes / Scripts{Core,Agents,UI,Network,Data} / Prefabs / Art / StreamingAssets）
- [ ] 装依赖：
  - `com.unity.nuget.newtonsoft-json`（已有 3.0.2）
  - NativeWebSocket（WebGL 可用的 WS 客户端）
  - DOTween（移动补间）
  - TextMeshPro + **NotoSansSC 中文字体**（做出 SDF 字体资源，中文对话的前提）
- [ ] 从 provenance 拷贝素材：`maze.json` → StreamingAssets；`frontend/static/assets/village/tilemap/`、`agents/`（6 角色 sprite + agent.json）、`agents_pool/` → Art/；`governance.json`、`story.json` 作数据参考
- [ ] **写回放器** `tools/mock_ws_server.py`：读 provenance `results/` 存档，按原节奏重放 init/snapshot/agent/chat_line/time/story/ping 消息

**里程碑 M0**：WebGL 空构建能跑 + 回放器能推消息。

### Phase 1：核心原型（约 2 周，第一优先级）

**1a. 网络层（~3 天）**
- [ ] `Data/WsMessage.cs`：按 CLAUDE.md §7 契约写 C# 消息类（agent / chat_line / time / story / ping / snapshot / done / error），字段以 main_script.html 实际解析为准
- [ ] `Network/WSClient.cs`：NativeWebSocket 连 `ws://<host>:5001/ws`、收包队列、`ping` 心跳
- [ ] **心跳看门狗**：20s 无消息判定断线 → 自动重连 → 重连后等 `snapshot` 追赶
- [ ] `Core/MessageDispatcher.cs`：按 type 分发事件，GameManager 持有以 `name` 为键的角色状态表
- [ ] URL 参数化：WebGL 嵌 iframe 时后端地址通过页面 query 参数（如 `?backend=ws://...`）传入——需与仝牧侧确认格式

**1b. 场景与角色（~4 天）**
- [ ] `AdvisoryRoom.unity` 主场景：读 `maze.json`（StreamingAssets）画网格地图（Unity Tilemap，1 unit = 1 tile）；退路是灰色底+网格线
- [ ] `Agents/AgentController.cs` + `AgentMovement.cs`：6 角色按 `coord` 定位、按 `path` 逐格移动；**移动队列化**（新 path 到达时先走完旧的，参照 Phaser）；按位移方向切 4 方向行走动画
- [ ] Agent.prefab：SpriteRenderer + Animator + 头顶名牌 + role_type 区分（ai_tool 视觉高亮）

**1c. 对话与叙事 UI（~3 天）**
- [ ] `ChatPanel.cs`：`chat_line` → 打字机气泡（Coroutine 逐字），speaker 名字/头像，ai_tool 高亮配色
- [ ] `ClockUI.cs`：`time` 消息 "20250213-09:30" → 顶部时钟
- [ ] `EventBanner.cs`：`story` → 顶部剧情横幅（事件类型着色，可点击看全文——对应 IVD 第 1 步"情境注入"）

**1d. 倾向可视化——评委核心，单独立项（~4 天）**
- [ ] `UI/TendencyLabel.cs`：每角色实时 4 维权重条（`value_tendency` 更新即刷新，带缓动过渡）
- [ ] `UI/TendencyChart.cs`：侧栏折线图——每角色每目标一条线 + **约束期望虚线（governance.json）+ 干预竖线（interventions）**，纯 UGUI 绘制，逻辑照搬 main_script.html 的 canvas 曲线
- [ ] 数据缓冲：Dispatcher 维护每角色倾向历史序列；重连后由 snapshot 补齐

**里程碑 M1**：连回放器跑通"模拟推进 → Unity 实时跟随"闭环，四项必须交互（移动/气泡/倾向标签/时钟横幅）全可演示。**到这里 demo 就能录**。

### Phase 2：打磨（约 2 周）

- [ ] 正式美术：tilemap 室内地图（会议室/资料室/休息区/走廊）、角色行走图、UI 皮肤统一
- [ ] 治理面板：Unity 内嵌滑条调约束权重 → `POST /api/goals`（对应 IVD 第 6 步"专家参与"）；"导出曲线"调 `GET /api/export-chart`
- [ ] `Network/APIClient.cs`：/api/goals GET/POST、export-chart（WebGL 注意 CORS，需与后端确认或开新标签页）
- [ ] WebGL 优化：Brotli 压缩、剥离未用引擎模块、**自定义 Loading 模板**、iframe 容器分辨率适配（容器尺寸要问仝牧）
- [ ] 稳定性：长时间运行内存观察、断网/后端中途退出等异常路径
- [ ] 演示脚本化：10 分钟完整动线（情境注入 → 对话 → 倾向变化 → 专家干预 → 收敛）

**里程碑 M2**：WebGL 构建 v1，可给仝牧嵌入、可录比赛视频。

### Phase 3：联调与交付（约 2~3 周，含缓冲）

- [ ] 与真后端联调：消息时序、重连稳定性、`--resume` 续跑后曲线连续性
- [ ] 与仝牧平台联调：iframe 嵌入、加载参数传递、**角色选中联动**（Unity 选中角色 → postMessage 通知治理平台展示决策痕迹/倾向曲线——需双方开会定死协议）
- [ ] 契约如有变更，同步更新 WsMessage.cs + CLAUDE.md §7/§8
- [ ] 交付：Build 目录 + 嵌入说明 + 演示视频素材

**里程碑 M3**：WebGL 构建在仝牧平台可用。比 11–12 月截止留出至少一个月缓冲。

## 四、时间线（2026-08-28 起）

| 时间 | 内容 | 里程碑 |
|------|------|--------|
| 8/28–9/2 | Phase 0：工程/依赖/素材/回放器 | M0 |
| 9/3–9/19 | Phase 1：网络层→场景角色→对话UI→倾向可视化 | M1 内部 demo |
| 9/20–10/10 | Phase 2：美术/治理面板/WebGL 优化 | M2 构建v1 |
| 10/11–10/31 | Phase 3：真后端联调 + 仝牧平台嵌入联动 | M3 交付 |
| 11 月 | 比赛材料、演示视频、缓冲期 | 截止 |

关键路径：**Phase 1d（倾向可视化）** 与 **Phase 3 的仝牧联动会**——后者建议尽早约，先定 iframe 参数和 postMessage 协议。

## 五、风险与对策

| 风险 | 对策 |
|------|------|
| WebGL 下 WS 库兼容性 | 第一天就用 NativeWebSocket 出空包验证 |
| 中文字体在 WebGL 缺字 | NotoSansSC SDF 全量打包，第一周完成 |
| provenance 契约还在改 | 只写一层 WsMessage 映射；每周对一次 main_script.html |
| 消息积压导致移动错乱 | 移动队列化 + 过期 path 丢弃（照 Phaser 行为） |
| 真模型不可用/太慢 | 回放器兼做演示保底 |
| 仝牧平台嵌入要求未知 | Phase 3 前提前确认 iframe 尺寸、参数、postMessage 协议 |

## 六、环境备忘

- MCP：Unity Editor 打开工程时，`com.jlceaser.unity-mcp-vibe` 在 `localhost:8080` 托管 MCP 服务（`/health` 可探活）；ZCode 已注册 `UnityMCP`（SSE）。
- provenance 后端启动：`python live_fastapi.py --name demo --start "20250213-09:30" --stride 2 --step 0 --port 5001`
- 权威参照文件：`provenance/provenance/frontend/templates/main_script.html`
