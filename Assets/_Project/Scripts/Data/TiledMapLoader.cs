using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace Mavis.Data
{
    /// <summary>
    /// 通用 Tiled 地图加载器：读取 tilemap.json，生成 Grid + Tilemap 渲染。
    /// tile 来源优先级：
    ///   1. TiledTilesetManifest（编辑期预生成的 Sprite/Tile 资产，运行时零创建，推荐）；
    ///   2. 回退：运行时从 StreamingAssets 解码 PNG 按需创建（开发保底）。
    /// Collisions 层生成 TilemapCollider2D；其余逻辑块层记录为坐标集合供 agent 事件查询。
    /// WebGL 约束：WebGL 构建下清单随包加载（推荐），回退路径走 UnityWebRequest；无线程。
    /// </summary>
    public class TiledMapLoader : MonoBehaviour
    {
        [Tooltip("StreamingAssets 下的相对路径")]
        public string mapPath = "Maps/tilemap.json";

        [Tooltip("回退路径：tileset 图片所在目录（相对 StreamingAssets）")]
        public string tilesetFolder = "Maps/Tilesets";

        [Tooltip("每格的世界单位数（sprite PPU，1 格 = 1 单位）")]
        public int pixelsPerUnit = 32;

        [Tooltip("构建完成后自动把主相机对准地图中心并整图取景")]
        public bool autoFrameCamera = true;

        /// <summary>逻辑块层名 → 该层占用的格子集合（Tiled 坐标，row 0 = 顶行）。</summary>
        public IReadOnlyDictionary<string, HashSet<Vector2Int>> LogicalCells => _logicalCells;
        private readonly Dictionary<string, HashSet<Vector2Int>> _logicalCells = new Dictionary<string, HashSet<Vector2Int>>();

        public TiledMap Map { get; private set; }
        public Transform MapRoot { get; private set; }

        // Tiled 中属于数据层的层名：不做可视渲染，只收集逻辑坐标
        private static readonly HashSet<string> DataLayers = new HashSet<string>
        {
            "Collisions", "Object Interaction Blocks", "Arena Blocks",
            "Sector Blocks", "World Blocks", "Spawning Blocks", "Special Blocks Registry"
        };

        private TiledTilesetManifest _manifest;
        // 回退路径用：tileset 名 → (localId → Tile)，按需创建
        private readonly Dictionary<string, Dictionary<int, Tile>> _tileCache = new Dictionary<string, Dictionary<int, Tile>>();
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private readonly System.Diagnostics.Stopwatch _loadTimer = new System.Diagnostics.Stopwatch();

        private IEnumerator Start()
        {
            _manifest = Resources.Load<TiledTilesetManifest>("TiledTilesetManifest");
            _loadTimer.Restart();

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_manifest == null)
            {
                yield return LoadWebGL();
                FinishBuild(Map);
                yield break;
            }
            yield return LoadMapJsonWebGL();
#else
            LoadMapJson();
            if (_manifest == null)
                LoadTexturesFromDisk();
#endif
            FinishBuild(Map);
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator LoadMapJsonWebGL()
        {
            string url = ToUrl(Path.Combine(Application.streamingAssetsPath, mapPath));
            var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[TiledMapLoader] 加载地图失败: {req.error} ({url})");
                yield break;
            }
            Map = JsonConvert.DeserializeObject<TiledMap>(req.downloadHandler.text);
        }

        private IEnumerator LoadWebGL()
        {
            yield return LoadMapJsonWebGL();
            if (Map == null) yield break;

            foreach (var ts in Map.tilesets)
            {
                string url = ToUrl(Path.Combine(Application.streamingAssetsPath,
                    Path.Combine(tilesetFolder, ts.ImageFileName)));
                var req = UnityWebRequestTexture.GetTexture(url);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[TiledMapLoader] 加载 tileset 失败（跳过）: {ts.ImageFileName} - {req.error}");
                    continue;
                }
                if (!_textures.ContainsKey(ts.name))
                    _textures[ts.name] = DownloadHandlerTexture.GetContent(req);
            }
        }
#endif

        private void LoadMapJson()
        {
            string mapFile = Path.Combine(Application.streamingAssetsPath, mapPath);
            if (!File.Exists(mapFile))
            {
                Debug.LogError($"[TiledMapLoader] 地图文件不存在: {mapFile}");
                return;
            }
            Map = JsonConvert.DeserializeObject<TiledMap>(File.ReadAllText(mapFile));
        }

        private void LoadTexturesFromDisk()
        {
            foreach (var ts in Map.tilesets)
            {
                if (_textures.ContainsKey(ts.name))
                    continue; // tilemap.json 里同名 tileset 声明多次，纹理只解码一次

                string png = Path.Combine(Application.streamingAssetsPath,
                    Path.Combine(tilesetFolder, ts.ImageFileName));
                if (!File.Exists(png))
                {
                    Debug.LogWarning($"[TiledMapLoader] 缺少 tileset（跳过）: {ts.ImageFileName}");
                    continue;
                }

                // linear=false：PNG 是 sRGB 颜色，须按 sRGB 采样，否则整体发灰
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                tex.LoadImage(File.ReadAllBytes(png));
                _textures[ts.name] = tex;
            }
        }

        private void FinishBuild(TiledMap map)
        {
            if (map == null) return;

            MapRoot = new GameObject("TiledMap").transform;
            MapRoot.SetParent(transform, false);
            MapRoot.gameObject.AddComponent<Grid>();

            int order = 0;
            int paintedCells = 0;
            for (int li = 0; li < map.layers.Count; li++)
            {
                var layer = map.layers[li];
                if (layer.type != "tilelayer" || !layer.visible)
                    continue;

                bool isDataLayer = DataLayers.Contains(layer.name);
                bool isForeground = layer.name.Contains("Foreground");
                bool isCollision = layer.name == "Collisions";

                var go = new GameObject(layer.name);
                go.transform.SetParent(MapRoot, false);
                var tilemap = go.AddComponent<Tilemap>();

                if (!isDataLayer)
                {
                    var renderer = go.AddComponent<TilemapRenderer>();
                    renderer.sortingOrder = order + (isForeground ? 100 : 0);
                }
                order++;

                // 碰撞层 tile 用整格碰撞体；数据层与渲染层 tile 均不产生碰撞
                var colliderType = isCollision ? Tile.ColliderType.Grid : Tile.ColliderType.None;
                var cells = new HashSet<Vector2Int>();

                for (int i = 0; i < layer.data.Count; i++)
                {
                    uint gid = (uint)layer.data[i];
                    if (gid == 0)
                        continue;

                    int x = i % layer.width;
                    int y = layer.height - 1 - i / layer.width; // Tiled row 0 在顶部，Unity y 向上

                    if (isDataLayer)
                        cells.Add(new Vector2Int(x, y));

                    var flags = gid & TiledTileFlags.Mask;
                    int gidNoFlags = (int)(gid & ~TiledTileFlags.Mask);

                    var tile = ResolveTile(map, gidNoFlags, colliderType);
                    if (tile == null)
                        continue; // 缺失纹理（如 blocks_2/3）：视觉缺块，但逻辑坐标已记录

                    // 带翻转标志或需要碰撞的 tile 用独立实例，避免污染共享原型的 transform/collider
                    if (flags == 0 && !isCollision)
                    {
                        tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                    }
                    else
                    {
                        var variant = Instantiate(tile);
                        variant.transform = GetTransformMatrix(flags);
                        variant.colliderType = colliderType;
                        tilemap.SetTile(new Vector3Int(x, y, 0), variant);
                    }
                    paintedCells++;
                }

                if (isDataLayer)
                {
                    _logicalCells[layer.name] = cells;
                    if (isCollision)
                        go.AddComponent<TilemapCollider2D>();
                }
            }

            Debug.Log($"[TiledMapLoader] 构建完成: {paintedCells} 格, tile 来源: {( _manifest != null ? "预生成清单" : "运行时创建")}, " +
                      $"耗时 {_loadTimer.ElapsedMilliseconds}ms");
            if (autoFrameCamera) FrameCamera(map);
        }

        /// <summary>gid 为去掉翻转标志后的 GID（含 firstgid 偏移）。清单优先，回退运行时创建。</summary>
        private Tile ResolveTile(TiledMap map, int gid, Tile.ColliderType colliderType)
        {
            foreach (var ts in map.tilesets)
            {
                if (gid < ts.firstgid || gid >= ts.firstgid + ts.tilecount)
                    continue;

                int localId = gid - ts.firstgid;

                if (_manifest != null)
                {
                    // 清单资产是共享资源，绝不可改写其 colliderType/transform
                    return _manifest.Get(ts.name, localId);
                }

                if (!_tileCache.TryGetValue(ts.name, out var cache))
                    _tileCache[ts.name] = cache = new Dictionary<int, Tile>();
                if (cache.TryGetValue(localId, out var cached))
                    return cached;

                if (!_textures.TryGetValue(ts.name, out var tex))
                    return null; // 纹理缺失（如 blocks_2/3）

                int cols = ts.columns > 0 ? ts.columns : ts.imagewidth / ts.tilewidth;
                int col = localId % cols;
                int row = localId / cols;
                int px = ts.margin + col * (ts.tilewidth + ts.spacing);
                int py = ts.imageheight - (ts.margin + row * (ts.tileheight + ts.spacing) + ts.tileheight);

                var sprite = Sprite.Create(tex, new Rect(px, py, ts.tilewidth, ts.tileheight),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                sprite.name = $"{ts.name}_{localId}";

                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.name = sprite.name;
                tile.colliderType = colliderType;
                cache[localId] = tile;
                return tile;
            }
            return null;
        }

        private static Matrix4x4 GetTransformMatrix(uint flags)
        {
            if (flags == 0)
                return Matrix4x4.identity;

            bool h = (flags & TiledTileFlags.HorizontalFlip) != 0;
            bool v = (flags & TiledTileFlags.VerticalFlip) != 0;
            bool d = (flags & TiledTileFlags.DiagonalFlip) != 0;

            var scale = new Vector3(h ? -1f : 1f, v ? -1f : 1f, 1f);
            var rot = d ? Quaternion.Euler(0f, 0f, -90f) : Quaternion.identity;
            return Matrix4x4.TRS(Vector3.zero, rot, scale);
        }

        private void FrameCamera(TiledMap map)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[TiledMapLoader] 未找到 Main Camera，跳过取景");
                return;
            }

            cam.orthographic = true;
            QualitySettings.antiAliasing = 0;
            float w = map.width * map.tilewidth / (float)pixelsPerUnit;
            float hgt = map.height * map.tileheight / (float)pixelsPerUnit;

            // 整图恰好显示：orthoSize = 半图高，4K UHD（2160 竖向像素）下地图纵向 24 格恰好撑满，横向完整落在画面内
            cam.orthographicSize = hgt * 0.5f;
            cam.transform.position = new Vector3(w * 0.5f, hgt * 0.5f, -10f);
            Debug.Log($"[TiledMapLoader] 相机取景: orthoSize={cam.orthographicSize:F2} (整图适配)");
        }

        /// <summary>把世界坐标换算为 Tiled 格子坐标（row 0 = 顶行），用于与 maze.json 地址对照。</summary>
        public Vector2Int WorldToTiledCell(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt(worldPos.x);
            return new Vector2Int(x, Map.height - 1 - Mathf.FloorToInt(worldPos.y));
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static string ToUrl(string path)
        {
            path = path.Replace('\\', '/');
            return path.StartsWith("http") ? path : "file:///" + path;
        }
#endif
    }
}
