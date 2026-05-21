using System.Numerics;
using Lumina.Excel.Sheets;

namespace AetherTrail;

public static class MapCoordinateConverter
{
    private const float MapTextureSize = 2048f;
    private const float MapTextureCenter = MapTextureSize * 0.5f;

    public static Vector2 WorldToMapNormalized(
        Vector3 worldPosition,
        MapTransformSnapshot transform)
    {
        Vector2 map = WorldToMap(
            worldPosition,
            transform.SizeFactor,
            transform.OffsetX,
            transform.OffsetY);

        return new Vector2(
            (map.X + MapTextureCenter) / MapTextureSize,
            (map.Y + MapTextureCenter) / MapTextureSize);
    }

    public static Vector2 WorldToMap(
        Vector3 worldPosition,
        Map map)
    {
        return WorldToMap(
            worldPosition,
            map.SizeFactor,
            map.OffsetX,
            map.OffsetY);
    }

    public static Vector2 WorldToMap(
        Vector3 worldPosition,
        float sizeFactor,
        float offsetX,
        float offsetY)
    {
        float scale = sizeFactor / 100f;
        float mapX = (worldPosition.X + offsetX) * scale;
        float mapY = (worldPosition.Z + offsetY) * scale;

        return new Vector2(mapX, mapY);
    }
}
