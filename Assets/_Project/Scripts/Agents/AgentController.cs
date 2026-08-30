using System.Collections.Generic;
using UnityEngine;
using Mavis.Data;

namespace Mavis.Agents
{
    /// <summary>
    /// 角色视觉与消息落位。帧动画照 Phaser anims 手动驱动：
    /// 4 方向（down/left/right/up，texture 行 0~3）× 走路序列 [0,1,2,1]（sprite.json，
    /// 第 003 帧与站立帧同图），10fps；站立显示第 1 列帧。
    /// 不用 Animator 状态机，直接切 SpriteRenderer.sprite，行为与 Phaser 一致且零资产依赖。
    /// </summary>
    public class AgentController : MonoBehaviour
    {
        public const int DirDown = 0, DirLeft = 1, DirRight = 2, DirUp = 3;
        static readonly string[] DirNames = { "down", "left", "right", "up" };
        static readonly int[] WalkSequence = { 0, 1, 2, 1 };
        const float FrameInterval = 0.1f; // 10fps，Phaser live 模式同值

        public string DisplayName { get; private set; }
        public string RoleType { get; private set; } = "user";
        public string LastAction { get; private set; } = "";
        public string LastLocation { get; private set; } = "";

        SpriteRenderer _sr;
        TextMesh _label;
        AgentMovement _movement;
        Dictionary<string, Sprite> _sprites;
        Sprite _fallbackSprite;
        int _dir = DirDown;
        bool _moving;
        int _frameIndex;
        float _frameTimer;
        bool _positioned;

        public void Init(string assetName)
        {
            DisplayName = assetName;
            _sr = GetComponent<SpriteRenderer>();
            _movement = GetComponent<AgentMovement>();
            _label = GetComponentInChildren<TextMesh>();
            LoadSprites(assetName);

            if (_label != null)
            {
                _label.text = assetName;
                ApplyLabelStyle();
            }
            ApplyFrame();
        }

        public void SetRoleType(string roleType)
        {
            if (string.IsNullOrEmpty(roleType) || RoleType == roleType) return;
            RoleType = roleType;
            if (_label != null) ApplyLabelStyle();
        }

        /// <summary>名牌样式：白色加粗，AI 工具角色淡蓝标出（Phaser 用 [AI] 前缀区分）。</summary>
        void ApplyLabelStyle()
        {
            _label.fontStyle = FontStyle.Bold;
            _label.color = RoleType == "ai_tool"
                ? new Color(0.55f, 0.78f, 1f)
                : new Color(0.97f, 0.97f, 0.97f);
        }

        /// <summary>
        /// agent 消息落位：首条消息先贴齐 coord；有 path 走 path（入队，不打断旧路径），
        /// 无 path 直接走向 coord —— 与 Phaser moveAgent 行为一致。
        /// </summary>
        public void OnAgentState(WsAgentMsg msg)
        {
            if (msg == null) return;

            if (msg.coord != null && msg.coord.Length >= 2)
            {
                var tile = new Vector2Int(msg.coord[0], msg.coord[1]);
                if (!_positioned)
                {
                    _movement.SnapTo(tile);
                    _positioned = true;
                }
                else if (msg.path == null || msg.path.Length == 0)
                {
                    _movement.MoveTo(tile);
                }
            }

            if (msg.path != null && msg.path.Length > 0)
                _movement.SetPath(msg.path);

            LastAction = msg.action ?? "";
            LastLocation = msg.location ?? "";
        }

        public void SnapTo(Vector2Int tile)
        {
            _movement.SnapTo(tile);
            _positioned = true;
        }

        /// <summary>移动方向与运动状态切换（AgentMovement 调用）。</summary>
        public void SetLocomotion(int dir, bool moving)
        {
            if (_dir == dir && _moving == moving) return;
            _dir = dir;
            _moving = moving;
            _frameIndex = 0;
            _frameTimer = 0f;
            ApplyFrame();
        }

        void Update()
        {
            if (!_moving) return;
            _frameTimer += Time.deltaTime;
            while (_frameTimer >= FrameInterval)
            {
                _frameTimer -= FrameInterval;
                _frameIndex = (_frameIndex + 1) % WalkSequence.Length;
                ApplyFrame();
            }
        }

        void LoadSprites(string assetName)
        {
            _sprites = new Dictionary<string, Sprite>();
            foreach (var s in Resources.LoadAll<Sprite>($"Agents/{assetName}/texture"))
                _sprites[s.name] = s;

            if (_sprites.Count == 0)
            {
                Debug.LogError($"[Agent] 角色贴图切片缺失: Agents/{assetName}/texture（先运行 MAVIS/Agents/Generate Assets），以洋红方块占位");
                _fallbackSprite = Sprite.Create(
                    Texture2D.whiteTexture, new Rect(0f, 0f, 32f, 32f),
                    new Vector2(0.5f, 0.5f), 32f);
                if (_sr != null) _sr.color = Color.magenta;
            }
        }

        void ApplyFrame()
        {
            if (_sr == null) return;
            int frame = _moving ? WalkSequence[_frameIndex] : 1;
            string key = $"{DirNames[_dir]}_{frame}";
            _sr.sprite = _sprites != null && _sprites.TryGetValue(key, out var s) && s != null
                ? s
                : _fallbackSprite;
        }
    }
}
