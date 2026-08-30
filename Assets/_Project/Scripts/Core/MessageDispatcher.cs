using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Mavis.Data;

namespace Mavis.Core
{
    /// <summary>
    /// 消息解析与分发：八类契约消息全部解析（解析层一次写全），
    /// v0.1 消费 agent/snapshot，其余以事件暴露 + 日志。
    /// 倾向历史按模拟时间去重、上限 200 点（照 main_script tendencyRecord）。
    /// </summary>
    public class MessageDispatcher
    {
        public event Action<WsAgentMsg> Agent;
        public event Action<WsSnapshotMsg> Snapshot;
        public event Action<WsTimeMsg> Time;
        public event Action<WsChatLineMsg> ChatLine;
        public event Action<WsStoryMsg> Story;
        public event Action Done;
        public event Action<string> Error;
        public event Action Init;

        const int TendencyHistoryCap = 200;

        public string CurrentTime { get; private set; }
        public IReadOnlyDictionary<string, List<TendencyPoint>> TendencyHistory => _tendencyHistory;

        readonly Dictionary<string, List<TendencyPoint>> _tendencyHistory =
            new Dictionary<string, List<TendencyPoint>>();

        public void Feed(string json)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Dispatcher] JSON 解析失败: {e.Message} | {Truncate(json)}");
                return;
            }

            string type = obj.Value<string>("type");
            switch (type)
            {
                case WsMessageType.Agent:
                {
                    var msg = obj.ToObject<WsAgentMsg>();
                    if (msg == null || string.IsNullOrEmpty(msg.name))
                    {
                        Debug.LogWarning("[Dispatcher] agent 消息缺 name，丢弃");
                        return;
                    }
                    RecordTendency(msg);
                    Agent?.Invoke(msg);
                    break;
                }
                case WsMessageType.Snapshot:
                {
                    var msg = obj.ToObject<WsSnapshotMsg>();
                    if (msg != null && !string.IsNullOrEmpty(msg.time)) CurrentTime = msg.time;
                    Snapshot?.Invoke(msg);
                    break;
                }
                case WsMessageType.Time:
                {
                    var msg = obj.ToObject<WsTimeMsg>();
                    if (msg != null) CurrentTime = msg.time;
                    Time?.Invoke(msg);
                    break;
                }
                case WsMessageType.ChatLine:
                    ChatLine?.Invoke(obj.ToObject<WsChatLineMsg>());
                    break;
                case WsMessageType.Story:
                    Story?.Invoke(obj.ToObject<WsStoryMsg>());
                    break;
                case WsMessageType.Done:
                    Done?.Invoke();
                    break;
                case WsMessageType.Error:
                    Error?.Invoke(obj.Value<string>("message"));
                    break;
                case WsMessageType.Init:
                    Init?.Invoke();
                    break;
                case WsMessageType.Ping:
                    break; // 心跳：WSClient 层已视为连接健康
                default:
                    Debug.LogWarning($"[Dispatcher] 未知消息 type={type}，原样忽略");
                    break;
            }
        }

        /// <summary>倾向演变记录：同模拟时间刷新、新时间追加（阶梯=行动变化点）。</summary>
        void RecordTendency(WsAgentMsg msg)
        {
            if (msg.value_tendency == null || msg.value_tendency.Count == 0) return;
            string t = msg.time ?? "";
            if (!_tendencyHistory.TryGetValue(msg.name, out var hist))
                _tendencyHistory[msg.name] = hist = new List<TendencyPoint>();
            if (hist.Count > 0 && hist[hist.Count - 1].time == t)
            {
                hist[hist.Count - 1].tendency = msg.value_tendency;
            }
            else
            {
                hist.Add(new TendencyPoint { time = t, tendency = msg.value_tendency });
                if (hist.Count > TendencyHistoryCap) hist.RemoveAt(0);
            }
        }

        static string Truncate(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 120 ? s : s.Substring(0, 120) + "...";
        }
    }
}
