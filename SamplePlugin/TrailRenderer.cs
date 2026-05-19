using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherTrail;

public class TrailRenderer
{
    public bool Enabled;

    public bool IsPlayerNearCurrentPath(Vector3 playerPosition, float maxDistance)
    {
        if (this.smoothedPath.Count == 0)
            return false;

        float maxDistanceSq = maxDistance * maxDistance;

        foreach (var point in this.smoothedPath)
        {
            if (Vector3.DistanceSquared(playerPosition, point.Position) <= maxDistanceSq)
                return true;
        }

        return false;
    }

    private List<TrailPoint> currentPath = new();
    private List<TrailPoint> smoothedPath = new();
    private List<TrailPoint> previousSmoothedPath = new();

    private DateTime pathTransitionStart = DateTime.MinValue;
    private const double PathTransitionSeconds = 0.22;

    private const float HidePassedDistance = 2.5f;

    public void SetPath(TrailPath path)
    {
        this.currentPath = path.Points;

        var newPath = ResampleTrailPath(
            ChaikinSmoothTrailPath(
                WeightedSmoothTrailPath(
                    SimplifyTrailPath(this.currentPath)
                ),
                2
            ),
            2.0f
        );

        if (PathsAreVisuallySimilar(this.smoothedPath, newPath, 1.25f))
        {
            this.smoothedPath = newPath;
            return;
        }

        this.previousSmoothedPath = new List<TrailPoint>(this.smoothedPath);
        this.smoothedPath = newPath;
        this.pathTransitionStart = DateTime.UtcNow;
    }

    private static bool PathsAreVisuallySimilar(List<TrailPoint> a, List<TrailPoint> b, float maxAverageDistance)
    {
        if (a.Count == 0 || b.Count == 0)
            return false;

        int sampleCount = Math.Min(20, Math.Max(a.Count, b.Count));
        float totalDistance = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            TrailPoint pointA = SamplePathByIndex(a, i, sampleCount);
            TrailPoint pointB = SamplePathByIndex(b, i, sampleCount);

            totalDistance += Vector3.Distance(pointA.Position, pointB.Position);
        }

        float averageDistance = totalDistance / sampleCount;

        return averageDistance <= maxAverageDistance;
    }

    public void ClearPath()
    {
        this.currentPath.Clear();
        this.smoothedPath.Clear();
    }



    public void Draw()
    {
        if (!Enabled)
            return;

        if (this.smoothedPath.Count == 0)
            return;

        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        var drawList = ImGui.GetBackgroundDrawList();
        var playerPos = player.Position;

        var displayPath = GetDisplayPath();

        for (int i = 0; i < displayPath.Count; i++)
        {
            var point = displayPath[i];
            Vector3 worldPos = point.Position;

            if (Vector3.Distance(playerPos, worldPos) < HidePassedDistance)
                continue;

            if (Plugin.GameGui.WorldToScreen(worldPos, out Vector2 screenPos))
            {
                float alpha = Math.Clamp(1.0f - i * 0.018f, 0.15f, 1.0f);
                float baseSize = Plugin.Instance.Configuration.TrailMarkerSize;
                float radius = Math.Clamp(baseSize - i * 0.04f, 5f, baseSize);

                Vector4 markerColor = point.IsGraphPoint
                    ? new Vector4(0.3f, 0.9f, 1.0f, alpha)
                    : new Vector4(0.15f, 0.35f, 1.0f, alpha * 0.55f);

                uint color = ImGui.ColorConvertFloat4ToU32(markerColor);

                drawList.AddCircleFilled(screenPos, radius, color);
            }
        }
    }

    private static bool PathIsMostlyGraph(List<TrailPoint> path)
    {
        if (path.Count == 0)
            return false;

        int graphPoints = 0;

        foreach (var point in path)
        {
            if (point.IsGraphPoint)
                graphPoints++;
        }

        return graphPoints >= path.Count * 0.6f;
    }

    private static List<TrailPoint> SimplifyTrailPath(List<TrailPoint> path)
    {
        if (path.Count <= 3)
            return new List<TrailPoint>(path);

        List<TrailPoint> simplified = new()
    {
        path[0]
    };

        const float minPointDistance = 4.0f;
        const float directionSimilarityThreshold = 0.94f;

        TrailPoint lastKept = path[0];

        for (int i = 1; i < path.Count - 1; i++)
        {
            TrailPoint current = path[i];
            TrailPoint next = path[i + 1];

            Vector3 fromLast = current.Position - lastKept.Position;
            Vector3 toNext = next.Position - current.Position;

            fromLast.Y = 0f;
            toNext.Y = 0f;

            float distanceFromLast = fromLast.Length();

            if (distanceFromLast < minPointDistance)
                continue;

            if (fromLast.LengthSquared() < 0.001f || toNext.LengthSquared() < 0.001f)
                continue;

            Vector3 dirA = Vector3.Normalize(fromLast);
            Vector3 dirB = Vector3.Normalize(toNext);

            float directionDot = Vector3.Dot(dirA, dirB);

            bool almostStraight = directionDot > directionSimilarityThreshold;

            if (!almostStraight || current.IsGraphPoint != next.IsGraphPoint)
            {
                simplified.Add(current);
                lastKept = current;
            }
        }

        simplified.Add(path[^1]);

        return simplified;
    }

    private List<TrailPoint> GetDisplayPath()
    {
        if (this.previousSmoothedPath.Count == 0 ||
            this.smoothedPath.Count == 0 ||
            this.pathTransitionStart == DateTime.MinValue)
        {
            return this.smoothedPath;
        }

        double elapsed = (DateTime.UtcNow - this.pathTransitionStart).TotalSeconds;
        float t = (float)Math.Clamp(elapsed / PathTransitionSeconds, 0.0, 1.0);

        t = EaseOutCubic(t);

        if (t >= 1.0f)
        {
            this.previousSmoothedPath.Clear();
            return this.smoothedPath;
        }

        return BlendPaths(this.previousSmoothedPath, this.smoothedPath, t);
    }

    private static float EaseOutCubic(float t)
    {
        return 1f - MathF.Pow(1f - t, 3f);
    }

    private static List<TrailPoint> BlendPaths(List<TrailPoint> from, List<TrailPoint> to, float t)
    {
        int count = Math.Max(from.Count, to.Count);

        if (count == 0)
            return new List<TrailPoint>();

        List<TrailPoint> blended = new(count);

        for (int i = 0; i < count; i++)
        {
            TrailPoint fromPoint = SamplePathByIndex(from, i, count);
            TrailPoint toPoint = SamplePathByIndex(to, i, count);

            blended.Add(new TrailPoint
            {
                Position = Vector3.Lerp(fromPoint.Position, toPoint.Position, t),
                IsGraphPoint = toPoint.IsGraphPoint
            });
        }

        return blended;
    }

    private static TrailPoint SamplePathByIndex(List<TrailPoint> path, int index, int targetCount)
    {
        if (path.Count == 0)
            return new TrailPoint();

        if (path.Count == 1 || targetCount <= 1)
            return path[0];

        float normalized = index / (float)(targetCount - 1);
        float sourceIndex = normalized * (path.Count - 1);

        int lower = (int)MathF.Floor(sourceIndex);
        int upper = Math.Min(lower + 1, path.Count - 1);

        float t = sourceIndex - lower;

        TrailPoint a = path[lower];
        TrailPoint b = path[upper];

        return new TrailPoint
        {
            Position = Vector3.Lerp(a.Position, b.Position, t),
            IsGraphPoint = a.IsGraphPoint && b.IsGraphPoint
        };
    }

    private static List<TrailPoint> ResampleTrailPath(List<TrailPoint> path, float spacing)
    {
        if (path.Count < 2)
            return new List<TrailPoint>(path);

        List<TrailPoint> result = new();

        result.Add(path[0]);

        float carriedDistance = 0f;

        for (int i = 0; i < path.Count - 1; i++)
        {
            TrailPoint a = path[i];
            TrailPoint b = path[i + 1];

            Vector3 segment = b.Position - a.Position;
            float segmentLength = segment.Length();

            if (segmentLength < 0.001f)
                continue;

            Vector3 direction = segment / segmentLength;

            float distanceAlongSegment = spacing - carriedDistance;

            while (distanceAlongSegment < segmentLength)
            {
                Vector3 interpolatedPosition = a.Position + direction * distanceAlongSegment;

                result.Add(new TrailPoint
                {
                    Position = interpolatedPosition,
                    IsGraphPoint = a.IsGraphPoint && b.IsGraphPoint
                });

                distanceAlongSegment += spacing;
            }

            carriedDistance = segmentLength - (distanceAlongSegment - spacing);

            if (carriedDistance >= spacing)
                carriedDistance = 0f;
        }

        result.Add(path[^1]);

        return result;
    }

    private static List<TrailPoint> WeightedSmoothTrailPath(List<TrailPoint> path)
    {
        if (path.Count < 3)
            return new List<TrailPoint>(path);

        List<TrailPoint> result = new()
    {
        path[0]
    };

        for (int i = 1; i < path.Count - 1; i++)
        {
            TrailPoint previous = path[i - 1];
            TrailPoint current = path[i];
            TrailPoint next = path[i + 1];

            Vector3 blendedPosition =
                previous.Position * 0.20f +
                current.Position * 0.60f +
                next.Position * 0.20f;

            result.Add(new TrailPoint
            {
                Position = blendedPosition,
                IsGraphPoint = current.IsGraphPoint
            });
        }

        result.Add(path[^1]);

        return result;
    }

    private static List<TrailPoint> ChaikinSmoothTrailPath(List<TrailPoint> path, int iterations)
    {
        if (path.Count < 3)
            return new List<TrailPoint>(path);

        List<TrailPoint> current = new(path);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            List<TrailPoint> next = new();

            next.Add(current[0]);

            for (int i = 0; i < current.Count - 1; i++)
            {
                TrailPoint a = current[i];
                TrailPoint b = current[i + 1];

                Vector3 q = Vector3.Lerp(a.Position, b.Position, 0.25f);
                Vector3 r = Vector3.Lerp(a.Position, b.Position, 0.75f);

                bool isGraph = a.IsGraphPoint && b.IsGraphPoint;

                next.Add(new TrailPoint
                {
                    Position = q,
                    IsGraphPoint = isGraph
                });

                next.Add(new TrailPoint
                {
                    Position = r,
                    IsGraphPoint = isGraph
                });
            }

            next.Add(current[^1]);

            current = next;
        }

        return current;
    }

    private static List<TrailPoint> SmoothTrailPath(List<TrailPoint> path)
    {
        if (path.Count < 4)
            return new List<TrailPoint>(path);

        List<TrailPoint> result = new();

        for (int i = 0; i < path.Count - 1; i++)
        {
            TrailPoint p0 = path[Math.Max(i - 1, 0)];
            TrailPoint p1 = path[i];
            TrailPoint p2 = path[i + 1];
            TrailPoint p3 = path[Math.Min(i + 2, path.Count - 1)];

            float segmentDistance = Vector3.Distance(p1.Position, p2.Position);
            int steps = Math.Clamp((int)(segmentDistance / 1.5f), 2, 8);

            for (int step = 0; step < steps; step++)
            {
                float t = step / (float)steps;

                Vector3 smoothedPosition = CatmullRom(
                    p0.Position,
                    p1.Position,
                    p2.Position,
                    p3.Position,
                    t
                );

                result.Add(new TrailPoint
                {
                    Position = smoothedPosition,
                    IsGraphPoint = p1.IsGraphPoint && p2.IsGraphPoint
                });
            }
        }

        result.Add(path[^1]);

        return result;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}
