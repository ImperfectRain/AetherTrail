using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail;

public static class NavPathfinder
{

    public static List<Vector3> FindGraphPathBetweenNodes(
    NavGraph graph,
    Vector3 startPosition,
    NavNode startNode,
    Vector3 endPosition,
    NavNode endNode)
    {
        var nodePath = AStar(graph, startNode, endNode);

        if (nodePath.Count == 0)
            return new List<Vector3>();

        List<Vector3> path = new();

        path.Add(startPosition + new Vector3(0f, 0.35f, 0f));

        foreach (var node in nodePath)
            path.Add(node.Position + new Vector3(0f, 0.35f, 0f));

        path.Add(endPosition + new Vector3(0f, 0.35f, 0f));

        return SmoothPath(SimplifyPath(path));
    }

    public static bool HasRoute(NavGraph graph, NavNode startNode, NavNode endNode)
    {
        return AStar(graph, startNode, endNode).Count > 0;
    }
    public static List<Vector3> FindGraphPath(NavGraph graph, Vector3 start, Vector3 end)
    {
        var startNode = graph.GetNearestNode(start);
        var endNode = graph.GetNearestNode(end);

        if (startNode == null || endNode == null)
            return new List<Vector3>();

        var nodePath = AStar(graph, startNode, endNode);

        if (nodePath.Count == 0)
            return new List<Vector3>();

        List<Vector3> path = new();

        path.Add(start + new Vector3(0f, 0.35f, 0f));

        foreach (var node in nodePath)
            path.Add(node.Position + new Vector3(0f, 0.35f, 0f));

        path.Add(end + new Vector3(0f, 0.35f, 0f));

        return SmoothPath(SimplifyPath(path));
    }

    private static List<NavNode> AStar(NavGraph graph, NavNode start, NavNode goal)
    {
        HashSet<string> open = new() { start.Id };
        HashSet<string> closed = new();

        Dictionary<string, string> cameFrom = new();
        Dictionary<string, float> gScore = new()
        {
            [start.Id] = 0f
        };

        Dictionary<string, float> fScore = new()
        {
            [start.Id] = Heuristic(start, goal)
        };

        while (open.Count > 0)
        {
            string currentId = open.OrderBy(id => fScore.GetValueOrDefault(id, float.MaxValue)).First();
            var current = graph.GetNode(currentId);

            if (current == null)
                break;

            if (current.Id == goal.Id)
                return ReconstructPath(graph, cameFrom, current.Id);

            open.Remove(current.Id);
            closed.Add(current.Id);

            foreach (string neighborId in current.Links)
            {
                if (closed.Contains(neighborId))
                    continue;

                var neighbor = graph.GetNode(neighborId);

                if (neighbor == null)
                    continue;

                int confidence = current.LinkConfidence.TryGetValue(neighbor.Id, out int value)
                    ? value
                    : 1;

                float tentativeG = gScore[current.Id] + GetTraversalCost(current.Position, neighbor.Position, confidence);

                if (!open.Contains(neighbor.Id))
                    open.Add(neighbor.Id);
                else if (tentativeG >= gScore.GetValueOrDefault(neighbor.Id, float.MaxValue))
                    continue;

                cameFrom[neighbor.Id] = current.Id;
                gScore[neighbor.Id] = tentativeG;
                fScore[neighbor.Id] = tentativeG + Heuristic(neighbor, goal);
            }
        }

        return new List<NavNode>();
    }

    private static float GetTraversalCost(Vector3 from, Vector3 to, int confidence)
    {
        Vector3 delta = to - from;

        float distance = delta.Length();
        float verticalDelta = MathF.Abs(delta.Y);

        delta.Y = 0f;
        float horizontalDistance = MathF.Max(delta.Length(), 0.1f);

        float slopeRatio = verticalDelta / horizontalDistance;

        float cost = distance;

        cost += verticalDelta * 3.0f;

        if (slopeRatio > 0.45f)
            cost += distance * 2.0f;

        if (distance > 16.0f)
            cost += distance * 1.5f;

        float confidenceDiscount = MathF.Min(confidence, 20) * 0.025f;

        cost *= 1.0f - confidenceDiscount;

        return MathF.Max(cost, distance * 0.5f);
    }

    private static float Heuristic(NavNode a, NavNode b)
    {
        return Vector3.Distance(a.Position, b.Position);
    }

    private static List<NavNode> ReconstructPath(NavGraph graph, Dictionary<string, string> cameFrom, string currentId)
    {
        List<NavNode> path = new();

        var current = graph.GetNode(currentId);

        if (current != null)
            path.Add(current);

        while (cameFrom.TryGetValue(currentId, out string? previousId))
        {
            currentId = previousId;
            var node = graph.GetNode(currentId);

            if (node != null)
                path.Add(node);
        }

        path.Reverse();
        return path;
    }

    private static List<Vector3> SimplifyPath(List<Vector3> path)
    {
        if (path.Count <= 2)
            return path;

        List<Vector3> simplified = new()
        {
            path[0]
        };

        const float minDistance = 8.0f;
        Vector3 lastKept = path[0];

        for (int i = 1; i < path.Count - 1; i++)
        {
            if (Vector3.Distance(lastKept, path[i]) >= minDistance)
            {
                simplified.Add(path[i]);
                lastKept = path[i];
            }
        }

        simplified.Add(path[^1]);

        return simplified;
    }

    private static List<Vector3> SmoothPath(List<Vector3> path)
    {
        if (path.Count < 2)
            return path;

        List<Vector3> smoothed = new();

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[i + 1];

            int steps = Math.Max(2, (int)(Vector3.Distance(a, b) / 3f));

            for (int step = 0; step < steps; step++)
            {
                float t = step / (float)steps;
                smoothed.Add(Vector3.Lerp(a, b, t));
            }
        }

        smoothed.Add(path[^1]);

        return smoothed;
    }
}
