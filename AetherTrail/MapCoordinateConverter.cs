using System.Numerics;
using Lumina.Excel.Sheets;

namespace AetherTrail;

public static class MapCoordinateConverter
{
    public static Vector2 WorldToMap(
        Vector3 worldPosition,
        Map map)
    {
        float scale = map.SizeFactor / 100f;

        float offsetX = map.OffsetX;
        float offsetY = map.OffsetY;

        float mapX = ((worldPosition.X + offsetX) * scale);
        float mapY = ((worldPosition.Z + offsetY) * scale);

        return new Vector2(mapX, mapY);
    }
}
