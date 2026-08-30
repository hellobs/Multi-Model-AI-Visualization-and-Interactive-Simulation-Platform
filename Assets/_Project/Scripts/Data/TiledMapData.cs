using System.Collections.Generic;

namespace Mavis.Data
{
    /// <summary>
    /// Tiled 编辑器导出 JSON 的数据模型，只映射 tilemap.json 实际用到的字段。
    /// </summary>
    public class TiledMap
    {
        public int width;
        public int height;
        public int tilewidth;
        public int tileheight;
        public List<TiledLayer> layers;
        public List<TiledTileset> tilesets;
    }

    public class TiledLayer
    {
        public string name;
        public string type;
        public bool visible;
        public float opacity = 1f;
        public int width;
        public int height;
        public List<int> data;
    }

    public class TiledTileset
    {
        public string name;
        public string image;
        public int firstgid;
        public int imagewidth;
        public int imageheight;
        public int columns;
        public int tilecount;
        public int tilewidth;
        public int tileheight;
        public int spacing;
        public int margin;

        /// <summary>Tiled 的 image 字段带仓库相对目录，运行时只按文件名解析。</summary>
        public string ImageFileName
        {
            get
            {
                int slash = image != null ? image.LastIndexOf('/') : -1;
                return slash >= 0 ? image.Substring(slash + 1) : image;
            }
        }
    }

    /// <summary>Tiled GID 高位的翻转/旋转标志。</summary>
    public static class TiledTileFlags
    {
        public const uint DiagonalFlip = 0x20000000u;
        public const uint VerticalFlip = 0x40000000u;
        public const uint HorizontalFlip = 0x80000000u;
        public const uint Mask = DiagonalFlip | VerticalFlip | HorizontalFlip;
    }
}
