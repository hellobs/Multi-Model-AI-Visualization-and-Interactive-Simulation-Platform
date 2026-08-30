using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;

namespace Mavis.Network
{
    public enum WsState { Idle, Connecting, Open, WaitingReconnect, Failed }

    /// <summary>
    /// WebGL 兼容 WebSocket 客户端（NativeWebSocket）。
    /// 主线程 Pump() 派发收包（WebGL 无线程）；心跳看门狗照 Phaser main_script：
    /// 服务端每 5s 发 ping，20s 无任何消息判死连接 → 3s 后重连；
    /// 10s 窗口内重连超过 6 次判定服务端不可用，停止重试。
    /// </summary>
    public class WSClient : IDisposable
    {
        const float DeadAfterSeconds = 20f;
        const float ReconnectDelay = 3f;
        const int MaxAttemptsPerWindow = 6;
        const float AttemptWindowSeconds = 10f;

        public WsState State { get; private set; } = WsState.Idle;
        public string Url => _url;

        /// <summary>任意消息原文（含 ping）都会触发；订阅方在主线程收到。</summary>
        public event Action<string> OnMessageRaw;
        public event Action<WsState> OnStateChanged;

        readonly string _url;
        readonly List<float> _attempts = new List<float>();
        WebSocket _ws;
        float _lastMessageAt;
        float _reconnectAt = -1f;
        bool _disposed;

        public WSClient(string url)
        {
            _url = url;
        }

        public void Connect()
        {
            if (_disposed) return;
            if (State == WsState.Connecting || State == WsState.Open) return;
            if (!RegisterAttempt())
            {
                SetState(WsState.Failed);
                Debug.LogError($"[WSClient] 重连次数超限（{MaxAttemptsPerWindow} 次/{AttemptWindowSeconds}s），停止重试: {_url}");
                return;
            }

            SetState(WsState.Connecting);
            _ws = new WebSocket(_url);

            _ws.OnOpen += () =>
            {
                _lastMessageAt = Time.realtimeSinceStartup;
                SetState(WsState.Open);
                Debug.Log($"[WSClient] 已连接: {_url}");
            };
            _ws.OnMessage += bytes =>
            {
                _lastMessageAt = Time.realtimeSinceStartup;
                string text = Encoding.UTF8.GetString(bytes);
                OnMessageRaw?.Invoke(text);
            };
            _ws.OnError += err =>
            {
                Debug.LogWarning($"[WSClient] 错误: {err}");
                ScheduleReconnect();
            };
            _ws.OnClose += code =>
            {
                Debug.LogWarning($"[WSClient] 连接关闭({code})");
                ScheduleReconnect();
            };

            _ = ConnectAsync();
        }

        async Task ConnectAsync()
        {
            try
            {
                await _ws.Connect();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WSClient] 连接失败: {e.Message}");
                ScheduleReconnect();
            }
        }

        /// <summary>主线程每帧调用：派发收包队列 + 看门狗 + 重连调度。</summary>
        public void Pump()
        {
            if (_disposed) return;

            // WebGL 用 jslib 回调直接派发，无消息队列可泵；编辑器/单机才需要 DispatchMessageQueue
#if !UNITY_WEBGL || UNITY_EDITOR
            if (_ws != null)
            {
                try { _ws.DispatchMessageQueue(); }
                catch (Exception e) { Debug.LogWarning($"[WSClient] 派发异常: {e.Message}"); }
            }
#endif

            float now = Time.realtimeSinceStartup;

            // 看门狗：连接显示 OPEN 但 20s 无消息（半开死连接）→ 强制关闭走重连
            if (State == WsState.Open && now - _lastMessageAt > DeadAfterSeconds)
            {
                Debug.LogWarning($"[WSClient] {DeadAfterSeconds}s 无消息，判定连接已死，强制重连");
                CloseSocket();
                ScheduleReconnect();
                return;
            }

            if (State == WsState.WaitingReconnect && now >= _reconnectAt)
                Connect();
        }

        void ScheduleReconnect()
        {
            if (_disposed || State == WsState.Failed) return;
            _reconnectAt = Time.realtimeSinceStartup + ReconnectDelay;
            SetState(WsState.WaitingReconnect);
        }

        bool RegisterAttempt()
        {
            float now = Time.realtimeSinceStartup;
            _attempts.RemoveAll(t => now - t > AttemptWindowSeconds);
            _attempts.Add(now);
            return _attempts.Count <= MaxAttemptsPerWindow;
        }

        async void CloseSocket()
        {
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    await _ws.Close();
            }
            catch { /* 关闭失败不影响重连流程 */ }
        }

        void SetState(WsState s)
        {
            if (State == s) return;
            State = s;
            OnStateChanged?.Invoke(s);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CloseSocket();
            _ws = null;
        }
    }
}
