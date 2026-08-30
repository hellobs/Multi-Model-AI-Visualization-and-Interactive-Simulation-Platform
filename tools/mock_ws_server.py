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
        asyncio.create_task(srv.run())
        asyncio.create_task(srv.pinger())
        print(f"[mock] 监听 ws://localhost:{args.port}/ws")
        await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())