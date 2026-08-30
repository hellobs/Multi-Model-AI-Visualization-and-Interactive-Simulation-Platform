using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Mavis.Data
{
    /// <summary>消息 type 常量（契约：mavis runtime/protocol.py，以 live_fastapi.py 实际发送为准）。</summary>
    public static class WsMessageType
    {
        public const string Init = "init";
        public const string Agent = "agent";
        public const string ChatLine = "chat_line";
        public const string Time = "time";
        public const string Story = "story";
        public const string Ping = "ping";
        public const string Snapshot = "snapshot";
        public const string Done = "done";
        public const string Error = "error";
    }

    /// <summary>
    /// 单个 agent 实时状态。字段全部可选（protocol.py TypedDict total=False），
    /// v0.1 只消费 name/coord/path/role_type，其余解析出来供后续 UI 直接接入。
    /// </summary>
    public class WsAgentMsg
    {
        public string type;
        public string name;
        public int[] coord;                        // [x, y] 格子坐标
        public int[][] path;                       // 寻路路径点（格子，逐格）
        public string action;                      // 当前动作描述
        public string location;                    // 地址（业务语义）
        public string currently;                   // 人设当前状态
        public string role_type;                   // "user" / "ai_tool"
        public float? goal_score;                  // 行动对约束的整体对齐度
        public Dictionary<string, float> goal_alignment;
        public Dictionary<string, float> value_tendency;   // IVD 核心观测：价值倾向
        public string time;                        // "20250213-12:42"
        public Dictionary<string, string> conversation;
        public Dictionary<string, string> description;
    }

    public class WsTimeMsg
    {
        public string type;
        public string time;
    }

    public class WsChatLineMsg
    {
        public string type;
        public string speaker;
        public string text;
    }

    public class WsStoryMsg
    {
        public string type;
        public string id;
        public string time;
        public string event_type;
        public string content;
        public List<string> targets;
    }

    public class WsErrorMsg
    {
        public string type;
        public string message;
    }

    /// <summary>全量快照：新连接/重连追赶。agents 为 {角色名: AgentState 子集}。</summary>
    public class WsSnapshotMsg
    {
        public string type;
        public string time;
        public Dictionary<string, WsAgentMsg> agents;
    }

    /// <summary>倾向历史点（曲线 UI 数据源，按模拟时间去重）。</summary>
    public class TendencyPoint
    {
        public string time;
        public Dictionary<string, float> tendency;
    }
}
