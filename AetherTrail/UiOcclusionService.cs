using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public static class UiOcclusionService
{
    private static readonly List<(Vector2 Min, Vector2 Max)> Rects = new();

    public static void BeginFrame()
    {
        Rects.Clear();
    }

    public static void AddRect(Vector2 min, Vector2 max)
    {
        Rects.Add((min, max));
    }

    public static bool Contains(Vector2 point)
    {
        foreach (var rect in Rects)
        {
            if (point.X >= rect.Min.X &&
                point.X <= rect.Max.X &&
                point.Y >= rect.Min.Y &&
                point.Y <= rect.Max.Y)
            {
                return true;
            }
        }

        return false;
    }
}
