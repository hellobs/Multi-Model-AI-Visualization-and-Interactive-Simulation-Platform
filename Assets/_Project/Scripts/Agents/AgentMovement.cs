using System.Collections.Generic;
using UnityEngine;
using Mavis.Data;

namespace Mavis.Agents
{
    /// <summary>
    /// 按 path 逐格移动，逐条翻译 Phaser main_script update 循环：
    /// - waypoint 队列：新 path 追加到队尾（先走完旧路），首点与队尾重合时去重；
    /// - 恒速 1.5 格/s（Phaser 48px/s @ 32px tile），按 deltaTime 折算，高刷新率不加速；
    /// - 偏差超过 3 格视为丢消息/重连残留，直接贴齐路径点再继续（不许飞穿地图）；
    /// - 按位移主轴切 4 方向动画，走完回站立帧。
    /// </summary>
    public class AgentMovement : MonoBehaviour
    {
        public float tilesPerSecond = 1.5f;
        public float realignTiles = 3f;

        public bool IsMoving => _waypoints.Count > 0;

        readonly List<Vector3> _waypoints = new List<Vector3>();
        AgentController _ctrl;
        int _lastDir = AgentController.DirDown;
        bool _wasMoving;

        void Awake()
        {
            _ctrl = GetComponent<AgentController>();
        }

        /// <summary>整条路径入队（格子坐标数组，元素 [x, y]）。</summary>
        public void SetPath(int[][] path)
        {
            if (path == null || path.Length == 0) return;
            var waypoints = new List<Vector3>(path.Length);
            foreach (var p in path)
            {
                if (p == null || p.Length < 2) continue;
                waypoints.Add(MapCoords.TileToWorld(p[0], p[1]));
            }
            if (waypoints.Count == 0) return;
            Enqueue(waypoints);
        }

        /// <summary>无 path 消息：直接走向该格子。</summary>
        public void MoveTo(Vector2Int tile)
        {
            Enqueue(new List<Vector3> { MapCoords.TileToWorld(tile.x, tile.y) });
        }

        /// <summary>贴齐（快照/首条消息）：清空队列直接落位。</summary>
        public void SnapTo(Vector2Int tile)
        {
            _waypoints.Clear();
            transform.position = MapCoords.TileToWorld(tile.x, tile.y);
            if (_ctrl != null) _ctrl.SetLocomotion(_lastDir, false);
            _wasMoving = false;
        }

        void Enqueue(List<Vector3> waypoints)
        {
            if (_waypoints.Count > 0)
            {
                Vector3 tail = _waypoints[_waypoints.Count - 1];
                if (waypoints[0] == tail) waypoints.RemoveAt(0); // 新路径起点=队尾，去重
                if (waypoints.Count == 0) return;
                _waypoints.AddRange(waypoints);
            }
            else
            {
                _waypoints.Clear();
                _waypoints.AddRange(waypoints);
            }
        }

        void Update()
        {
            float step = tilesPerSecond * Time.deltaTime;

            // 一帧内可连续消耗多个 waypoint（贴齐不耗步长）
            int guard = 0;
            while (_waypoints.Count > 0 && step > 0f && guard++ < 64)
            {
                Vector3 target = _waypoints[0];
                Vector3 pos = transform.position;
                Vector3 delta = target - pos;
                delta.z = 0f;
                float dist = delta.magnitude;

                if (dist > realignTiles)
                {
                    // 丢消息/重连后位置漂移过大：贴齐路径点再继续
                    transform.position = target;
                    _waypoints.RemoveAt(0);
                    continue;
                }

                if (dist <= step)
                {
                    transform.position = target;
                    _waypoints.RemoveAt(0);
                    step -= dist;
                    continue;
                }

                Vector3 dir = delta / dist;
                transform.position = pos + dir * step;
                step = 0f;

                int d = Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
                    ? (dir.x > 0f ? AgentController.DirRight : AgentController.DirLeft)
                    : (dir.y > 0f ? AgentController.DirUp : AgentController.DirDown);
                if (d != _lastDir || !_wasMoving)
                {
                    _lastDir = d;
                    _wasMoving = true;
                    if (_ctrl != null) _ctrl.SetLocomotion(d, true);
                }
            }

            if (_waypoints.Count == 0 && _wasMoving)
            {
                _wasMoving = false;
                if (_ctrl != null) _ctrl.SetLocomotion(_lastDir, false); // 走完回站立帧
            }
        }
    }
}
