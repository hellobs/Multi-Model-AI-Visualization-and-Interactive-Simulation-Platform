using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mavis.Data.EditorTools
{
    /// <summary>
    /// 编辑期预生成 tileset 清单：读 tilemap.json，为全部 tileset 的所有 tile 预创建
    /// Sprite/Tile 并固化到 TiledTilesetManifest.asset（含 PNG 导入设置一并处理）。
    /// 高度超过单张纹理上限（4096）的 tileset 自动纵向分块生成中间纹理。
    /// 地图或 PNG 变化时自动重建。菜单 Mavis/Map/重新生成 Tileset 清单 可手动触发。
    /// </summary>
    public static class TiledTilesetManifestGenerator
    {
        private const string ManifestPath = "Assets/_Project/Resources/TiledTilesetManifest.asset";
        private const string MapJsonPath = "Assets/StreamingAssets/Maps/tilemap.json";
        private const string TilesetsDir = "Assets/_Project/Art/Tilesets";
        private const string ChunksDir = TilesetsDir + "/Generated";
        private const int MaxTextureSize = 4096;

        [InitializeOnLoadMethod]
        private static void AutoRun()
        {
            EditorApplication.delayCall += () =>
            {
                try { GenerateIfNeeded(); }
                catch (System.Exception e) { Debug.LogError($"[Tileset清单] 自动生成失败: {e}"); }
            };
        }

        [MenuItem("Mavis/Map/重新生成 Tileset 清单")]
        private static void ForceRegenerate()
        {
            try { Generate(); }
            catch (System.Exception e) { Debug.LogError($"[Tileset清单] 生成失败: {e}"); }
        }

        private static void GenerateIfNeeded()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TiledTilesetManifest>(ManifestPath);
            string hash = ComputeSourceHash();
            if (existing != null && existing.sourceHash == hash)
                return; // 无变化
            Generate();
        }

        private static void Generate()
        {
            string dir = Path.GetDirectoryName(ManifestPath)?.Replace('\\', '/');
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var manifest = ScriptableObject.CreateInstance<TiledTilesetManifest>();
            AssetDatabase.DeleteAsset(ManifestPath);
            AssetDatabase.CreateAsset(manifest, ManifestPath);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var map = JsonConvert.DeserializeObject<TiledMap>(
                File.ReadAllText(Path.Combine(projectRoot, MapJsonPath)));

            var pending = new Dictionary<string, TiledTileset>(); // 同名声明去重（blocks_1 声明多次），取最大 tilecount
            foreach (var ts in map.tilesets)
            {
                if (pending.TryGetValue(ts.name, out var p))
                {
                    if (ts.tilecount > p.tilecount) pending[ts.name] = ts;
                }
                else pending[ts.name] = ts;
            }

            int created = 0;
            foreach (var ts in pending.Values)
            {
                string pngPath = $"{TilesetsDir}/{ts.ImageFileName}";
                ConfigureImporter(pngPath, ts.imagewidth, ts.imageheight);

                // 全量预创建：0..tilecount-1 每个 tile 都生成，改图换块无需重新生成清单
                bool needsChunks = ts.imageheight > MaxTextureSize;
                int rowsPerChunk = needsChunks ? MaxTextureSize / ts.tileheight : int.MaxValue;
                var chunkTextures = new Dictionary<int, Texture2D>();

                var entry = new TiledTilesetManifest.Entry { tileset = ts.name };
                entry.tiles.Capacity = ts.tilecount;

                int cols = ts.columns > 0 ? ts.columns : ts.imagewidth / ts.tilewidth;
                for (int localId = 0; localId < ts.tilecount; localId++)
                {
                    int col = localId % cols;
                    int row = localId / cols;
                    int px = ts.margin + col * (ts.tilewidth + ts.spacing);

                    Texture2D tex;
                    int py;
                    if (!needsChunks)
                    {
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
                        py = ts.imageheight - (ts.margin + row * (ts.tileheight + ts.spacing) + ts.tileheight);
                    }
                    else
                    {
                        int chunkIndex = row / rowsPerChunk;
                        if (!chunkTextures.TryGetValue(chunkIndex, out tex))
                        {
                            tex = GetOrGenerateChunk(projectRoot, pngPath, ts, chunkIndex, rowsPerChunk);
                            chunkTextures[chunkIndex] = tex;
                        }
                        int rowInChunk = row - chunkIndex * rowsPerChunk;
                        int chunkH = tex != null ? tex.height : 0;
                        py = chunkH - (rowInChunk * (ts.tileheight + ts.spacing) + ts.tileheight);
                    }

                    if (tex == null)
                    {
                        Debug.LogWarning($"[Tileset清单] 找不到纹理（跳过该 tileset）: {pngPath}");
                        break;
                    }

                    var sprite = Sprite.Create(tex, new Rect(px, py, ts.tilewidth, ts.tileheight),
                        new Vector2(0.5f, 0.5f), 32f);
                    sprite.name = $"{ts.name}_{localId}";
                    AssetDatabase.AddObjectToAsset(sprite, manifest);

                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = sprite;
                    tile.name = sprite.name;
                    tile.colliderType = Tile.ColliderType.None;
                    AssetDatabase.AddObjectToAsset(tile, manifest);

                    entry.tiles.Add(tile);
                    created++;
                }
                if (entry.tiles.Count > 0)
                    manifest.entries.Add(entry);
            }

            manifest.sourceHash = ComputeSourceHash();
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Tileset清单] 生成完成: {created} 个 tile → {ManifestPath}");
        }

        /// <summary>把超高 tileset 源图按 rowsPerChunk 行切成分块 PNG（一次生成，后续复用）。</summary>
        private static Texture2D GetOrGenerateChunk(string projectRoot, string sourcePngPath,
            TiledTileset ts, int chunkIndex, int rowsPerChunk)
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, ChunksDir));
            string chunkPath = $"{ChunksDir}/{ts.ImageFileName}.part{chunkIndex}.png";
            string chunkFull = Path.Combine(projectRoot, chunkPath);
            string sourceFull = Path.Combine(projectRoot, sourcePngPath);

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(chunkPath);
            if (existing != null) return existing;
            if (File.Exists(chunkFull))
            {
                AssetDatabase.ImportAsset(chunkPath);
                existing = AssetDatabase.LoadAssetAtPath<Texture2D>(chunkPath);
                if (existing != null) return existing;
            }

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            src.LoadImage(File.ReadAllBytes(sourceFull));

            int step = ts.tileheight + ts.spacing;
            int yTopStart = ts.margin + chunkIndex * rowsPerChunk * step;
            int yTopEnd = Mathf.Min(ts.margin + (chunkIndex + 1) * rowsPerChunk * step - ts.spacing, ts.imageheight);
            int chunkH = yTopEnd - yTopStart;
            int srcW = src.width;
            if (chunkH <= 0) return null;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var chunk = new Texture2D(src.width, chunkH, TextureFormat.RGBA32, false, false);
            chunk.ReadPixels(new Rect(0, src.height - yTopEnd, src.width, chunkH), 0, 0);
            chunk.Apply();
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(src);

            File.WriteAllBytes(chunkFull, chunk.EncodeToPNG());
            Object.DestroyImmediate(chunk);
            AssetDatabase.ImportAsset(chunkPath);
            ConfigureImporter(chunkPath, srcW, chunkH);
            Debug.Log($"[Tileset清单] 生成分块: {Path.GetFileName(chunkFull)} ({srcW}x{chunkH})");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(chunkPath);
        }


        private static void ConfigureImporter(string pngPath, int srcW, int srcH)
        {
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null) return;
            bool dirty = false;
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (importer.wrapMode != TextureWrapMode.Clamp) { importer.wrapMode = TextureWrapMode.Clamp; dirty = true; }
            if (importer.maxTextureSize < Mathf.Max(srcW, srcH)) { importer.maxTextureSize = MaxTextureSize; dirty = true; }
            if (importer.npotScale != TextureImporterNPOTScale.None) { importer.npotScale = TextureImporterNPOTScale.None; dirty = true; }
            if (dirty) importer.SaveAndReimport();
        }

        private static string ComputeSourceHash()
        {
            const int PipelineVersion = 2; // 生成逻辑变更时 +1，强制重建清单
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            var sb = new StringBuilder();
            sb.Append(PipelineVersion);
            sb.Append(File.ReadAllText(Path.Combine(projectRoot, MapJsonPath)));
            foreach (var png in Directory.GetFiles(Path.Combine(projectRoot, TilesetsDir), "*.png"))
                sb.Append(Path.GetFileName(png)).Append(File.GetLastWriteTimeUtc(png).Ticks);
            using (var sha = SHA1.Create())
                return System.Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }
}
