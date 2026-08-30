using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mavis.Data
{
    /// <summary>
    /// 预生成的 tileset 清单：编辑期由 TiledTilesetManifestGenerator 依据 tilemap.json
    /// 为实际用到的 GID 预创建 Sprite/Tile 资产。运行时直接查表渲染，无需解码 PNG。
    /// </summary>
    public class TiledTilesetManifest : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string tileset;
            /// <summary>以 localId 为下标的数组，未用到的槽位为 null。</summary>
            public List<Tile> tiles = new List<Tile>();
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>生成依据（tilemap.json 内容 + PNG 时间戳）的指纹，用于变更检测。</summary>
        public string sourceHash;

        public Tile Get(string tilesetName, int localId)
        {
            var entry = entries.Find(e => e.tileset == tilesetName);
            if (entry == null || localId < 0 || localId >= entry.tiles.Count)
                return null;
            return entry.tiles[localId];
        }
    }
}
