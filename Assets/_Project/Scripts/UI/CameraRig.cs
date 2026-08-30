using System.Collections.Generic;
using UnityEngine;
using Mavis.Core;
using Mavis.Data;

namespace Mavis.Overlays
{
    /// <summary>
    /// 相机取景：整图竖向撑满，地图贴画面左侧，右侧整块留白给 HUD 面板。
    /// 画面宽度不足以容纳全图宽时（窄窗），地图仍贴左并允许右侧被裁切。
    /// 无滚轮缩放、无跟随——演示画面稳定。
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public float followLerp = 5f;

        Camera _cam;
        float _baseOrtho = 12f; // 整图适配基准（地图 24 格高 → orthoSize 12）
        TiledMapLoader _mapLoader;

        public static CameraRig Create(AgentRegistry registry, TiledMapLoader mapLoader)
        {
            var go = new GameObject("CameraRig");
            var rig = go.AddComponent<CameraRig>();
            rig._mapLoader = mapLoader;
            return rig;
        }

        void Start()
        {
            _cam = Camera.main;
            if (_cam == null) { enabled = false; return; }
            _cam.orthographic = true;
            if (_mapLoader != null) _mapLoader.autoFrameCamera = false;
        }

        void Update()
        {
            if (_cam == null) return;

            _cam.orthographicSize = _baseOrtho;
            float mapW = _mapLoader != null && _mapLoader.Map != null ? _mapLoader.Map.width : 27f;
            float mapH = _mapLoader != null && _mapLoader.Map != null ? _mapLoader.Map.height : 24f;

            float visW = _baseOrtho * 2f * _cam.aspect;
            // 地图居中（面板已移除，画面左右对称）
            float cx = visW >= mapW ? mapW * 0.5f : mapW * 0.5f;

            var target = new Vector3(cx, mapH * 0.5f, -10f);
            _cam.transform.position = Vector3.Lerp(
                _cam.transform.position, target, followLerp * Time.deltaTime);
        }
    }
}
