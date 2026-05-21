using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    private static NavNode? GetStableStartNode(NavGraph graph, uint territoryId, Vector3 start, float snapDistance)
    {
        if (territoryId != LastPathTerritoryId)
        {
            LastPathStartNodeId = null;
            LastPathTerritoryId = territoryId;
        }

        var nearestNode = graph.GetNearestNode(start, snapDistance);

        if (nearestNode == null)
        {
            LastPathStartNodeId = null;
            return null;
        }

        if (LastPathStartNodeId == null)
        {
            LastPathStartNodeId = nearestNode.Id;
            return nearestNode;
        }

        var previousNode = graph.GetNode(LastPathStartNodeId);

        if (previousNode == null)
        {
            LastPathStartNodeId = nearestNode.Id;
            return nearestNode;
        }

        float previousDistance = Vector3.Distance(start, previousNode.Position);
        float nearestDistance = Vector3.Distance(start, nearestNode.Position);

        const float switchImprovementDistance = 5.0f;

        if (nearestNode.Id != previousNode.Id &&
            nearestDistance + switchImprovementDistance < previousDistance)
        {
            LastPathStartNodeId = nearestNode.Id;
            return nearestNode;
        }

        if (previousDistance <= snapDistance * 1.35f)
            return previousNode;

        LastPathStartNodeId = nearestNode.Id;
        return nearestNode;
    }

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
        const float targetSearchDistance = 90.0f;
        const float directRemainderPenalty = 2.5f;

        var startNode = GetStableStartNode(graph, territoryId, start, graphSnapDistance);

        if (startNode == null)
            return BuildDirectPath(start, end);

        var bestCandidate = FindBestTargetCandidate(
            graph,
            start,
            startNode,
            end,
            targetSearchDistance,
            directRemainderPenalty);

        if (bestCandidate == null)
            return BuildDirectPath(start, end);

        if (bestCandidate.Result.Points.Count == 0)
            return BuildDirectPath(start, end);

        if (bestCandidate.DirectDistanceToTarget <= graphSnapDistance)
            return BuildGraphPath(bestCandidate.Result.Points);

        var result = new TrailPath();

        foreach (var point in bestCandidate.Result.Points)
        {
            result.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = true
            });
        }

        var directRemainder = FlagPathGenerator.GeneratePath(bestCandidate.Node.Position, end);

        foreach (var point in directRemainder)
        {
            result.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = false
            });
        }

        return result;
    }

    private static TargetCandidate? FindBestTargetCandidate(
        NavGraph graph,
        Vector3 start,
        NavNode startNode,
        Vector3 end,
        float targetSearchDistance,
        float directRemainderPenalty)
    {
        const int maxCandidates = 24;

        TargetCandidate? best = null;
        float targetSearchDistanceSq = targetSearchDistance * targetSearchDistance;

        var candidates = graph.Nodes
            .Select(node => new
            {
                Node = node,
                DistanceSq = Vector3.DistanceSquared(node.Position, end)
            })
            .Where(candidate => candidate.DistanceSq <= targetSearchDistanceSq)
            .OrderBy(candidate => candidate.DistanceSq)
            .Take(maxCandidates);

        foreach (var candidateInfo in candidates)
        {
            var candidate = candidateInfo.Node;

            var path = NavPathfinder.FindGraphPathBetweenNodesWithCost(
                graph,
                start,
                startNode,
                candidate.Position,
                candidate);

            if (!path.Success)
                continue;

            float directDistance = MathF.Sqrt(candidateInfo.DistanceSq);
            float score = path.Cost + directDistance * directRemainderPenalty;

            if (best == null || score < best.Score)
            {
                best = new TargetCandidate
                {
                    Node = candidate,
                    Result = path,
                    DirectDistanceToTarget = directDistance,
                    Score = score
                };
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

    private static TrailPath BuildGraphPath(List<Vector3> points)
    {
        var result = new TrailPath();

        foreach (var point in points)
        {
            result.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = true
            });
        }

        return result;
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
        public NavNode Node { get; init; } = null!;
        public NavPathResult Result { get; init; } = new();
        public float DirectDistanceToTarget { get; init; }
        public float Score { get; init; }
    }
}
