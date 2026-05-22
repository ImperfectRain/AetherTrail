using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    public static int GetLinkCount(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        int count = 0;

        foreach (var node in graph.Nodes)
            count += node.Links.Count;

        return count;
    }

    public static TrailPath GetPath(uint territoryId, Vector3 start, Vector3 end)
    {
        var graph = GetOrLoadGraph(territoryId);

        if (IsPlayerFlying())
            return BuildDirectPath(start, end);

        graph = BuildModeFilteredGraph(graph, NavTraversalMode.Ground);

        if (graph.Nodes.Count < 2)
            return BuildDirectPath(start, end);

        const float graphSnapDistance = 18.0f;
        const float graphRejoinSearchDistance = 48.0f;
        const float directRejoinPenalty = 1.25f;
        const float directRemainderPenalty = 2.5f;

        var bestCandidate = FindBestRouteCandidate(
            graph,
            start,
            end,
            graphSnapDistance,
            graphRejoinSearchDistance,
            directRejoinPenalty,
            directRemainderPenalty);

        if (bestCandidate == null)
            return BuildDirectPath(start, end);

        if (bestCandidate.Result.Points.Count == 0)
            return BuildDirectPath(start, end);

        var result = new TrailPath();

        if (bestCandidate.DirectDistanceFromStart > graphSnapDistance)
            AppendTrailPoints(result, FlagPathGenerator.GeneratePath(start, bestCandidate.StartNode.Position), false);
        else
            AppendTrailPoints(result, new[] { start, bestCandidate.StartNode.Position }, true);

        AppendTrailPoints(result, bestCandidate.Result.Points, true);

        if (bestCandidate.DirectDistanceToTarget <= graphSnapDistance)
            return result;

        var directRemainder = FlagPathGenerator.GeneratePath(bestCandidate.Node.Position, end);

        AppendTrailPoints(result, directRemainder, false);

        return result;
    }

    private static TargetCandidate? FindBestRouteCandidate(
        NavGraph graph,
        Vector3 start,
        Vector3 end,
        float graphSnapDistance,
        float rejoinSearchDistance,
        float directRejoinPenalty,
        float directRemainderPenalty)
    {
        const int maxStartCandidates = 4;
        const int maxCandidates = 24;

        TargetCandidate? best = null;
        float graphSnapDistanceSq = graphSnapDistance * graphSnapDistance;
        float rejoinSearchDistanceSq = rejoinSearchDistance * rejoinSearchDistance;

        var nearbyStartCandidates = graph.Nodes
            .Select(node => new
            {
                Node = node,
                DistanceSq = Vector3.DistanceSquared(node.Position, start)
            })
            .Where(candidate => candidate.DistanceSq <= graphSnapDistanceSq)
            .OrderBy(candidate => candidate.DistanceSq)
            .Take(maxStartCandidates)
            .ToList();

        var startCandidates = nearbyStartCandidates.Count > 0
            ? nearbyStartCandidates
            : graph.Nodes
            .Select(node => new
            {
                Node = node,
                DistanceSq = Vector3.DistanceSquared(node.Position, start)
            })
            .Where(candidate => candidate.DistanceSq <= rejoinSearchDistanceSq)
            .OrderBy(candidate => candidate.DistanceSq)
            .Take(maxStartCandidates)
            .ToList();

        var targetCandidates = graph.Nodes
            .Select(node => new
            {
                Node = node,
                DistanceSq = Vector3.DistanceSquared(node.Position, end)
            })
            .OrderBy(candidate => candidate.DistanceSq)
            .Take(maxCandidates)
            .ToList();

        foreach (var startInfo in startCandidates)
        {
            float directDistanceFromStart = MathF.Sqrt(startInfo.DistanceSq);

            foreach (var targetInfo in targetCandidates)
            {
                var targetNode = targetInfo.Node;

                var path = NavPathfinder.FindGraphPathBetweenNodesWithCost(
                    graph,
                    startInfo.Node.Position,
                    startInfo.Node,
                    targetNode.Position,
                    targetNode);

                if (!path.Success)
                    continue;

                float directDistanceToTarget = MathF.Sqrt(targetInfo.DistanceSq);
                float score =
                    path.Cost +
                    directDistanceFromStart * directRejoinPenalty +
                    directDistanceToTarget * directRemainderPenalty;

                if (best == null || score < best.Score)
                {
                    best = new TargetCandidate
                    {
                        StartNode = startInfo.Node,
                        Node = targetNode,
                        Result = path,
                        DirectDistanceFromStart = directDistanceFromStart,
                        DirectDistanceToTarget = directDistanceToTarget,
                        Score = score
                    };
                }
            }
        }

        return best;
    }

    private static TrailPath BuildDirectPath(Vector3 start, Vector3 end)
    {
        var result = new TrailPath();

        foreach (var point in FlagPathGenerator.GeneratePath(start, end))
        {
            result.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = false
            });
        }

        return result;
    }

    private static void AppendTrailPoints(TrailPath path, IEnumerable<Vector3> points, bool isGraphPoint)
    {
        foreach (var point in points)
        {
            if (path.Points.Count > 0 &&
                Vector3.DistanceSquared(path.Points[^1].Position, point) < 0.01f)
            {
                continue;
            }

            path.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = isGraphPoint
            });
        }
    }

    private static NavGraph BuildModeFilteredGraph(NavGraph source, NavTraversalMode mode)
    {
        var result = new NavGraph();

        var allowedIds = source.Nodes
            .Where(node => node.TraversalMode == mode)
            .Select(node => node.Id)
            .ToHashSet();

        foreach (var node in source.Nodes)
        {
            if (!allowedIds.Contains(node.Id))
                continue;

            result.Nodes.Add(new NavNode
            {
                Id = node.Id,
                Position = node.Position,
                TraversalMode = node.TraversalMode,
                Links = node.Links
                    .Where(linkId => allowedIds.Contains(linkId))
                    .ToList(),
                LinkConfidence = node.LinkConfidence
                    .Where(pair => allowedIds.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            });
        }

        return result;
    }

    private sealed class TargetCandidate
    {
        public NavNode StartNode { get; init; } = null!;
        public NavNode Node { get; init; } = null!;
        public NavPathResult Result { get; init; } = new();
        public float DirectDistanceFromStart { get; init; }
        public float DirectDistanceToTarget { get; init; }
        public float Score { get; init; }
    }
}
