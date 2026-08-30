using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Mavis.Agents;

namespace Mavis.EditorTools
{
    /// <summary>
    /// 角色资产生成：把 Resources/Agents/<名>/texture.png 按 3列×4行 32px 网格切片
    /// （行序 down/left/right/up，列 0/1/2；站立帧=列1，走路序列由 AgentController 控制），
    /// 并生成通用 Agent.prefab（SpriteRenderer + AgentMovement + AgentController + TextMesh 名牌）。
    /// 菜单: Mavis/Agents/Generate Assets —— 素材或 prefab 结构变更后重跑即可，幂等。
    /// </summary>
    public static class AgentAssetGenerator
    {
        const string AgentsRoot = "Assets/_Project/Resources/Agents";
        const string PrefabPath = "Assets/_Project/Resources/Agent.prefab";
        const int Tile = 32;
        static readonly string[] Rows = { "down", "left", "right", "up" };

        [MenuItem("Mavis/Agents/Generate Assets")]
        public static void Generate()
        {
            AssetDatabase.Refresh();
            int sliced = 0;
            foreach (var dir in Directory.GetDirectories(AgentsRoot).OrderBy(d => d))
            {
                string texPath = Path.Combine(dir, "texture.png").Replace('\\', '/');
                if (!File.Exists(texPath)) continue;
                SliceTexture(texPath);
                sliced++;
            }
            if (sliced == 0) { Debug.LogError($"[AgentAssetGenerator] {AgentsRoot} 下没找到 texture.png"); return; }
            BuildPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log($"[AgentAssetGenerator] 完成: {sliced} 张贴图切片 + Agent.prefab");
        }

        static void SliceTexture(string texPath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) { Debug.LogError($"[AgentAssetGenerator] 无法加载: {texPath}"); return; }
            if (tex.width != Tile * 3 || tex.height != Tile * 4)
            {
                Debug.LogError($"[AgentAssetGenerator] 尺寸不符(期望 96×128): {texPath} 实际 {tex.width}×{tex.height}");
                return;
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.spritePixelsPerUnit = Tile;   // 1 格 = 1 unit，与 TiledMapLoader 一致
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 128;

            var metas = new List<SpriteMetaData>();
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 3; col++)
                    metas.Add(new SpriteMetaData
                    {
                        name = $"{Rows[row]}_{col}",
                        // Unity 切片 rect 原点在左下，texture 行 0(down) 在顶部 → y 翻转
                        rect = new Rect(col * Tile, tex.height - (row + 1) * Tile, Tile, Tile),
                        pivot = new Vector2(0.5f, 0.5f),
                        alignment = (int)SpriteAlignment.Center,
                    });
            importer.spritesheet = metas.ToArray();
            importer.SaveAndReimport();
        }

        static void BuildPrefab()
        {
            var go = new GameObject("Agent");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 50;   // 地表 tilemap 之上、Foreground(+100) 之下
            go.AddComponent<AgentMovement>();
            go.AddComponent<AgentController>();

            var labelGo = new GameObject("Nameplate");
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<TextMesh>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 28;
            label.characterSize = 0.13f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            label.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 60;

            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
        }
    }
}