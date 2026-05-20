using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public static class FlagPathGenerator
{
    public static List<Vector3> GeneratePath(Vector3 start, Vector3 end, int pointCount = 20)
    {
        List<Vector3> points = new();

        for (int i = 1; i <= pointCount; i++)
        {
            float t = i / (float)pointCount;

            Vector3 point = Vector3.Lerp(start, end, t);

            point.Y += 0.35f;

            points.Add(point);
        }

        return points;
    }
}
