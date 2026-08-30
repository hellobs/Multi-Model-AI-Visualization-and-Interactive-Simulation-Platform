<!-- language switch -->
English | [简体中文](./README_zh.md)

---

# Multi-Model AI Visualization and Interactive Simulation Platform

A **Unity-based visualization and interaction front-end** for multi-agent social
simulation. It renders, in real time, the latent decision process of AI agents
driven by the *MAVIS* simulation framework — mapping agent perception,
decision-making, reflection, and movement onto a visual, inspectable canvas.

This repository is the **Unity-based visualization platform** for multi-agent
social simulation. Its counterpart is *Provenance* — the **Phaser.js (Web-based)
visualization platform** for the same simulation. The two platforms are **peers**:
distinct presentation layers that render identical simulator output, share the
same backend entry point and WebSocket message contract, and differ only in the
rendering technology. Their joint purpose is to make the otherwise-abstract
"value formation" process of AI agents **observable, auditable, and governable**,
in support of the *Internal Value Development (IVD)* research program.

---

## 1. Background and Motivation

Most current AI alignment and governance approaches focus on **outputs** —
whether a system's final response conforms to predefined rules. This project
supports a complementary view: **process alignment** — can we observe *how* a
value-related judgment is formed inside the system, and audit that process
after the fact?

To that end, the simulation stack is organized into three layers:

| Layer | Repository | Responsibility |
|---|---|---|
| Engine (simulation logic) | [`mavisframework`](https://github.com/hellobs/mavis) | Agents, memory, reflection, goal-scoring, decision-trace export. Rendering- and transport-agnostic. |
| Shared backend | `live_fastapi.py` (FastAPI + WebSocket); physically in the [Provenance](https://github.com/hellobs/provenance) repo | Drives a simulation and broadcasts a structured message contract over WebSocket. |
| Visualization · **Web** | [Provenance](https://github.com/hellobs/provenance) — **Phaser.js** | Renders the contract in the browser: map layers, sprites, nameplates, goal-constraint panel. |
| Visualization · **Unity (this repo)** | — | Renders the same contract in a Unity 2-D scene: agent movement, directional frame animation, nameplates, and grid→world mapping. |

The Decoupling principle is deliberate: **simulation logic never speaks to the
renderer**. Every front-end consumes the same contract, which is what allows the
same simulation to be displayed by Provenance, this Unity client, or a future
governance dashboard without modifying the engine.

---

## 2. Runtime Architecture

```
   shared backend (single entry point)
   ┌──────────────────────────────────────────────┐
   │ live_fastapi.py   (FastAPI + WebSocket)      │
   │   └─ orchestrated by mavisframework          │── WebSocket contract ──┐
   │       agents · memory · reflection · goals   │                        │
   │ tools/mock_ws_server.py  (checkpoint replay) │                        │
   └──────────────────────────────────────────────┘                        ▼
                                                              ┌────────────────────────────┐
                                                              │ Unity client (this repo)   │
                                                              │ WSClient ── MessageDispatcher│
                                                              │   └─ AgentRegistry          │
                                                              │       └─ AgentController    │
                                                              │           (frame animation) │
                                                              └────────────────────────────┘
```

At runtime the client opens a WebSocket connection to
`ws://<host>:<port>/ws`, parses the incoming contract, and turns every
`agent` message into a positioned, animated character on a tile grid.

### 2.1 Message Contract

The client subscribes to (and logs) every message type; `agent` and `snapshot`
drive the character system. The contract is defined in `mavis/runtime/protocol.py` and emitted by the shared backend `live_fastapi.py`:

| Type | Purpose | Consumed by v0.1 |
|---|---|---|
| `init` | connection greeting | log |
| `agent` | per-agent state `{coord, path, action, location, currently, role_type, goal_score, goal_alignment, value_tendency, time, conversation, description}` | character system |
| `time` | simulation clock tick | log / tendency history |
| `chat_line` | a spoken line `{speaker, text}` | log |
| `story` | scenario event `{event_type, content, targets}` | log |
| `snapshot` | full-state catch-up `{time, agents:{name:…}}` | immediate snap-to (reconnect fast-forward) |
| `done` | simulation ended | log |
| `ping` | keep-alive (server ~5 s) | watchdog |

### 2.2 Two Data Sources

- **Live backend** (primary): the shared backend `live_fastapi.py` (in the
  Provenance repo) runs a simulation with a real LLM backend.
- **Checkpoint replay** (development / fallback): `tools/mock_ws_server.py`
  replays archived `simulate-*.json` checkpoints from `provenance/results/checkpoints/`
  as the same contract, with a configurable replay speed and loop mode.

Both feed the exact same pipeline, so switching between them requires no client
change.

---

## 3. Agents and Visual Pipeline

### 3.1 Roles (investment-advisory scenario)

| Asset name | 中文别名 | Role |
|---|---|---|
| `AI Advisor` | AI 投顾助手 | AI investment tool (role_type `ai_tool`) |
| `Daniel Shen` | 沈砚之 | Chief investment advisor |
| `Kevin Su` | 苏清越 | Quantitative analyst |
| `Michael Chen` | 陈慕白 | Research analyst |
| `Wendy Lin` | 林晚晴 | Risk control |
| `Mr. Zhou` | 老周 | Retail investor |

Characters are not hard-coded: they are instantiated on first appearance from
any `agent` message (via `Resources.Load("Agent")`), so the client is agnostic
to the scenario's cast. `agent_alias.json` maps Chinese archive names to English
asset names for replay compatibility.

### 3.2 Visual rendering

- **Frame animation**: each character sheet is a 3×4 grid of 32×32 tiles,
  rows `down / left / right / up`, columns walk frames `[0, 1, 2, 1]` at ~10 fps;
  the standing frame is column 1. Animation is applied manually
  (`SpriteRenderer.sprite`) rather than through an Animator state machine, so the
  behaviour exactly matches the Phaser reference implementation and carries no
  extra asset dependencies.
- **Nameplates**: `TextMesh` (built-in legacy font). The whole v0.1 UI is English
  to avoid bundling a CJK font; `ai_tool` characters are tinted blue.
- **Movement** is translated line-by-line from the Phaser client: a waypoint queue
  (new paths appended), constant speed of **1.5 tiles/s**, and a realignment rule
  snap a character back to the target cell when the positional drift exceeds 3 tiles
  (recovers from lost messages and reconnect).

### 3.3 Coordinate system

The tile-grid convention is shared with the Tiled map loader: Tiled row 0 is at
the top, while Unity's world `y` increases upward:

```
world = ( x + 0.5,  mapHeight - 1 - y + 0.5 )
```

---

## 4. Quick Start

### 4.1 Dependencies

- Unity 2022 LTS (2D project template).
- C# dependencies: `com.unity.nuget.newtonsoft-json 3.0.2` (declared in
  `Packages/manifest.json`).
- **`nativewebsocket`** is referenced for WebGL compatibility; see
  *Compatibility notes* below if compilation fails in an offline environment.

### 4.2 Start a data source

Replay (recommended for development):

```bash
python tools/mock_ws_server.py            # default: gtc-demo14, port 5001
python tools/mock_ws_server.py --checkpoint gtc-demo14 --speed 2 --loop
```

Live backend:

```bash
cd ../../provenance/provenance
python live_fastapi.py --name demo --start "20250213-09:30" --stride 2 --step 0 --port 5001
```

### 4.3 Run the Unity client

1. Open the project in Unity.
2. Generate sliced sprites and the shared `Agent.prefab`:
   **Menu → MAVIS → Agents → Generate Assets**.
3. Press **Play**. The client auto-boots a `SimulationClient`, connects to
   `ws://localhost:5001/ws` by default, and renders the agents as they move.

---

## 5. Project Layout

```
Assets/_Project/Scripts/
  Core/            # SimulationClient (boot/assembly), MessageDispatcher,
                   #                  AgentRegistry
  Network/         # WSClient (watchdog, reconnect)
  Agents/          # AgentController (frame animation), AgentMovement
  Data/            # WsMessages (contract POCOs), MapCoords, TiledMap*
  Editor/          # AgentAssetGenerator (slice + prefab)
Assets/_Project/Resources/Agents/<name>/   # character sheets
Assets/StreamingAssets/
  agent_alias.json # zh→en name mapping
  Maps/            # tile maps (separate work stream)
tools/mock_ws_server.py   # checkpoint replay server
docs/               # development plan, handoff notes
```

---

## 6. Status and Roadmap

Current scope (**v0.1** — message-driven agent movement) is deliberately narrow:

- **In scope**: connect to backend, drive 6 agents across the map by `path`,
  manual 4-direction frame animation, nameplates, reconnect/watchdog, and the
  two data sources (live + replay).
- **Out of scope for v0.1**: chat panel, governance-constraint panel, value-tendency
  chart UI, clock/banner, CJK fonts, WebGL build packaging.

`MessageDispatcher` already accumulates a **tendency history** (deduplicated by
simulation time, capped at 200 samples) from `value_tendency` and `goal_alignment`
on every `agent` message; this buffer is the intended data source for the future
value-tendency chart, which will surface how a role's internal goals evolve — the
core evidence for "value formation is governable."

Planned next steps: realtime tendency visualization, governance-constraint
interaction, and embedding the client inside the governance platform as an
observable replay view.

---

## 7. Related Work

- [MAVIS framework](https://github.com/hellobs/mavis) — the simulation engine
  (agents, memory, reflection, goal scoring, decision-trace export).
- [Provenance](https://github.com/hellobs/provenance) — the **Phaser.js (Web)**
  visualization platform for the same simulation, and the location of the shared
  FastAPI backend entry point.
- Governance and Decision-Making Platform — the upstream view where the exported
  decision traces (`category`, `risk_level`, `tags`) are classified and reviewed
  by experts.

---

## 8. Compatibility Notes

- The WebSocket transport targets WebGL. In offline environments where the
  `nativewebsocket` git package cannot be fetched, drop the single-file
  `NativeWebSocket.cs` into `Assets/_Project/Plugins/` and remove the git
  dependency from `Packages/manifest.json`.
- Several `interiors_*` tilesets in the source maps are taller than conventional
  power-of-two sizes; they are present for the Phaser client and may need to be
  split when edited in the Tiled editor.

## 9. License

Third-party assets (character sprites, tile sets) are used under their
respective licenses. The simulation logic and dashboard pipelines originate from
the MAVIS/Provenance research line; see those repositories and `docs/` for
details.