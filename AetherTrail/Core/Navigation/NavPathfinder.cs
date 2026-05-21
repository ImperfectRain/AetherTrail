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
        var result = FindGraphPathBetweenNodesWithCost(
            graph,
            startPosition,
            startNode,
            endPosition,
            endNode);

        return result.Success
            ? result.Points
            : new List<Vector3>();
    }

    public static NavPathResult FindGraphPathBetweenNodesWithCost(
    NavGraph graph,
    Vector3 startPosition,
    NavNode startNode,
    Vector3 endPosition,
    NavNode endNode)
    {
        var search = AStar(graph, startNode, endNode);

        if (!search.Success || search.Nodes.Count == 0)
        {
            return new NavPathResult
            {
                Success = false,
                FailureReason = search.FailureReason
            };
        }

        List<Vector3> path = new();

        path.Add(startPosition + new Vector3(0f, 0.35f, 0f));

        foreach (var node in search.Nodes)
            path.Add(node.Position + new Vector3(0f, 0.35f, 0f));

        path.Add(endPosition + new Vector3(0f, 0.35f, 0f));

        return new NavPathResult
        {
            Success = true,
            Points = SmoothPath(SimplifyPath(path)),
            Cost = search.Cost
        };
    }

    public static bool HasRoute(NavGraph graph, NavNode startNode, NavNode endNode)
    {
        return AStar(graph, startNode, endNode).Success;
    }

    public static List<Vector3> FindGraphPath(NavGraph graph, Vector3 start, Vector3 end)
    {
        var startNode = graph.GetNearestNode(start);
        var endNode = graph.GetNearestNode(end);

        if (startNode == null || endNode == null)
            return new List<Vector3>();

        var search = AStar(graph, startNode, endNode);

        if (!search.Success || search.Nodes.Count == 0)
            return new List<Vector3>();

        List<Vector3> path = new();

        path.Add(start + new Vector3(0f, 0.35f, 0f));

        foreach (var node in search.Nodes)
            path.Add(node.Position + new Vector3(0f, 0.35f, 0f));

        path.Add(end + new Vector3(0f, 0.35f, 0f));

        return SmoothPath(SimplifyPath(path));
    }

    private static NavSearchResult AStar(NavGraph graph, NavNode start, NavNode goal)
    {
        PriorityQueue<string, float> open = new();
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

        open.Enqueue(start.Id, fScore[start.Id]);

        while (open.Count > 0)
        {
            string currentId = open.Dequeue();

            if (closed.Contains(currentId))
                continue;

            var current = graph.GetNode(currentId);

            if (current == null)
                continue;

            if (current.Id == goal.Id)
            {
                return new NavSearchResult
                {
                    Success = true,
                    Nodes = ReconstructPath(graph, cameFrom, current.Id),
                    Cost = gScore.GetValueOrDefault(current.Id, 0f)
                };
            }

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
                    : NavConfidence.ImportedConfidence();

                float tentativeG = gScore[current.Id] + GetTraversalCost(current.Position, neighbor.Position, confidence);

                if (tentativeG >= gScore.GetValueOrDefault(neighbor.Id, float.MaxValue))
                    continue;

                cameFrom[neighbor.Id] = current.Id;
                gScore[neighbor.Id] = tentativeG;
                fScore[neighbor.Id] = tentativeG + Heuristic(neighbor, goal);
                open.Enqueue(neighbor.Id, fScore[neighbor.Id]);
            }
        }

        return new NavSearchResult
        {
            Success = false,
            FailureReason = "No connected graph route found."
        };
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

        confidence = NavConfidence.Clamp(confidence);

        if (confidence < NavConfidence.Imported)
            cost += distance * 1.5f;
        else if (confidence < NavConfidence.NewLocal)
            cost += distance * 0.65f;
        else if (confidence >= NavConfidence.Trusted)
            cost *= 0.86f;
        else if (confidence >= NavConfidence.NewLocal)
            cost *= 0.94f;

        return MathF.Max(cost, distance * 0.75f);
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

    private sealed class NavSearchResult
    {
        public bool Success { get; init; }
        public List<NavNode> Nodes { get; init; } = new();
        public float Cost { get; init; }
        public string FailureReason { get; init; } = string.Empty;
    }
}
