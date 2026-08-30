using UnityEngine;

namespace Mavis.Data
{
    /// <summary>
    /// 格子坐标 ↔ 世界坐标换算，约定与 TiledMapLoader 一致：
    /// Tiled row 0 在顶部、Unity y 向上，1 格 = 1 unit，角色站在格心。
    /// 地图未加载前用 tilemap.json 的高度兜底。
    /// </summary>
    public static class MapCoords
    {
        // tilemap.json 实际尺寸 27×24（864×768 @32px），仅作地图加载前的兜底值
        static int _mapHeight = 24;

        public static int MapHeight => _mapHeight;

        public static void SetMapHeight(int height)
        {
            if (height > 0) _mapHeight = height;
        }

        public static Vector3 TileToWorld(int tx, int ty)
        {
            return new Vector3(tx + 0.5f, _mapHeight - 1 - ty + 0.5f, 0f);
        }

        public static Vector2Int WorldToTile(Vector3 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x),
                _mapHeight - 1 - Mathf.FloorToInt(world.y));
        }
    }
}
