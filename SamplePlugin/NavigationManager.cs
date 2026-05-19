using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Linq;

namespace AetherTrail;

public static class NavigationManager
{
    
    private static readonly Dictionary<uint, NavGraph> Graphs = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    private static float NodeSpacing => Plugin.Instance.Configuration.NodeSpacing;
    private static float CornerNodeSpacing => Plugin.Instance.Configuration.CornerNodeSpacing;
    private static float DirectionChangeThreshold => Plugin.Instance.Configuration.DirectionChangeThreshold;
    private static float TeleportResetDistance => Plugin.Instance.Configuration.TeleportResetDistance;
    private static float SessionAttachDistance => Plugin.Instance.Configuration.SessionAttachDistance;


    private static Vector3? LastRecordedPosition;
    private static Vector3? LastMovementDirection;
    private static string? LastRecordedNodeId;
    private static uint LastTerritoryId;
    private static string? LastPathStartNodeId;
    private static uint LastPathTerritoryId;

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
    public static int GetNodeCount(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);
        return graph.Nodes.Count;
    }

    public static NavGraph GetGraph(uint territoryId)
    {
        return GetOrLoadGraph(territoryId);
    }

    public static void ReloadGraphs()
    {
        Graphs.Clear();

        LastRecordedPosition = null;
        LastMovementDirection = null;
        LastRecordedNodeId = null;
        LastTerritoryId = 0;
        LastPathStartNodeId = null;
        LastPathTerritoryId = 0;
    }

    public static GraphPruneResult PruneGraph(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        graph.Nodes.RemoveAll(node => !IsValidNodePosition(node.Position));

        int originalNodeCount = graph.Nodes.Count;
        int removedBrokenLinks = 0;
        int removedSelfLinks = 0;
        int removedDuplicateLinks = 0;

        HashSet<string> validIds = graph.Nodes
            .Select(n => n.Id)
            .ToHashSet();

        foreach (var node in graph.Nodes)
        {
            int before = node.Links.Count;

            node.Links.RemoveAll(linkId => !validIds.Contains(linkId));
            removedBrokenLinks += before - node.Links.Count;

            before = node.Links.Count;

            node.Links.RemoveAll(linkId => linkId == node.Id);
            removedSelfLinks += before - node.Links.Count;

            before = node.Links.Count;

            node.Links.RemoveAll(linkId =>
            {
                var linkedNode = graph.GetNode(linkId);

                if (linkedNode == null)
                    return true;

                return !IsTraversableLink(node.Position, linkedNode.Position);
            });

            removedBrokenLinks += before - node.Links.Count;

            before = node.Links.Count;

            var distinctLinks = node.Links.Distinct().ToList();
            node.Links.Clear();
            node.Links.AddRange(distinctLinks);

            removedDuplicateLinks += before - node.Links.Count;


        }

        foreach (var node in graph.Nodes)
        {
            foreach (var key in node.LinkConfidence.Keys.ToList())
            {
                if (!node.Links.Contains(key))
                {
                    node.LinkConfidence.Remove(key);
                    continue;
                }

                node.LinkConfidence[key] = Math.Max(1, node.LinkConfidence[key] - 1);
            }

            foreach (string linkId in node.Links)
            {
                if (!node.LinkConfidence.ContainsKey(linkId))
                    node.LinkConfidence[linkId] = 1;
            }
        }

        graph.Nodes.RemoveAll(node => node.Links.Count == 0);

        int removedIsolatedNodes = originalNodeCount - graph.Nodes.Count;

        int mergedDuplicateNodes = MergeDuplicateNodes(graph);

        SaveGraph(territoryId, graph);

        return new GraphPruneResult
        {
            RemovedIsolatedNodes = removedIsolatedNodes,
            RemovedBrokenLinks = removedBrokenLinks,
            RemovedSelfLinks = removedSelfLinks,
            RemovedDuplicateLinks = removedDuplicateLinks,
            MergedDuplicateNodes = mergedDuplicateNodes,
            RemainingNodes = graph.Nodes.Count,
            RemainingLinks = graph.Nodes.Sum(n => n.Links.Count)
        };
    }

    private static int MergeDuplicateNodes(NavGraph graph)
    {
        const float mergeDistance = 2.0f;
        int mergedCount = 0;

        bool mergedSomething;

        do
        {
            mergedSomething = false;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var a = graph.Nodes[i];

                for (int j = i + 1; j < graph.Nodes.Count; j++)
                {
                    var b = graph.Nodes[j];

                    if (Vector3.Distance(a.Position, b.Position) > mergeDistance)
                        continue;

                    MergeNodeInto(graph, source: b, target: a);
                    graph.Nodes.RemoveAt(j);

                    mergedCount++;
                    mergedSomething = true;
                    break;
                }

                if (mergedSomething)
                    break;
            }
        }
        while (mergedSomething);

        foreach (var node in graph.Nodes)
        {
            node.Links.RemoveAll(linkId => linkId == node.Id);

            var distinctLinks = node.Links.Distinct().ToList();
            node.Links.Clear();
            node.Links.AddRange(distinctLinks);
        }

        return mergedCount;
    }

    private static void MergeNodeInto(NavGraph graph, NavNode source, NavNode target)
    {
        foreach (string linkId in source.Links)
        {
            if (linkId == target.Id)
                continue;

            var linkedNode = graph.GetNode(linkId);

            if (linkedNode == null)
                continue;

            linkedNode.Links.RemoveAll(id => id == source.Id);

            if (linkedNode.Id != target.Id && !linkedNode.Links.Contains(target.Id))
                linkedNode.Links.Add(target.Id);

            if (!target.Links.Contains(linkedNode.Id))
                target.Links.Add(linkedNode.Id);
        }

        foreach (var node in graph.Nodes)
        {
            for (int i = 0; i < node.Links.Count; i++)
            {
                if (node.Links[i] == source.Id)
                    node.Links[i] = target.Id;
            }
        }
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

        if (graph.Nodes.Count < 2)
            return BuildDirectPath(start, end);

        const float graphSnapDistance = 18.0f;

        var startNode = GetStableStartNode(graph, territoryId, start, graphSnapDistance);
        var endNode = graph.GetNearestNode(end, graphSnapDistance);

        if (startNode == null)
            return BuildDirectPath(start, end);

        if (endNode != null)
        {
            var graphPath = NavPathfinder.FindGraphPathBetweenNodes(graph, start, startNode, end, endNode);

            if (graphPath.Count > 0)
                return BuildGraphPath(graphPath);
        }

        var bestExitNode = graph.Nodes
    .OrderBy(n => Vector3.Distance(n.Position, end))
    .FirstOrDefault(n => NavPathfinder.HasRoute(graph, startNode, n));

        if (bestExitNode == null)
            return BuildDirectPath(start, end);

        var partialGraphPath = NavPathfinder.FindGraphPathBetweenNodes(
            graph,
            start,
            startNode,
            bestExitNode.Position,
            bestExitNode
        );

        if (partialGraphPath.Count == 0)
            return BuildDirectPath(start, end);

        var result = new TrailPath();

        foreach (var point in partialGraphPath)
        {
            result.Points.Add(new TrailPoint
            {
                Position = point,
                IsGraphPoint = true
            });
        }

        var directRemainder = FlagPathGenerator.GeneratePath(bestExitNode.Position, end);

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

    private static void LinkNodes(NavNode a, NavNode b)
    {
        if (!IsTraversableLink(a.Position, b.Position))
            return;

        if (!a.Links.Contains(b.Id))
            a.Links.Add(b.Id);

        if (!b.Links.Contains(a.Id))
            b.Links.Add(a.Id);

        IncrementLinkConfidence(a, b.Id);
        IncrementLinkConfidence(b, a.Id);
    }

    private static void IncrementLinkConfidence(NavNode node, string linkedNodeId)
    {
        if (!node.LinkConfidence.ContainsKey(linkedNodeId))
            node.LinkConfidence[linkedNodeId] = 0;

        node.LinkConfidence[linkedNodeId]++;
    }

    private static bool IsTraversableLink(Vector3 a, Vector3 b)
    {
        const float maxLinkDistance = 24.0f;
        const float maxVerticalDelta = 7.0f;
        const float maxSlopeRatio = 1.10f;

        Vector3 delta = b - a;

        float totalDistance = delta.Length();

        if (totalDistance > maxLinkDistance)
            return false;

        float verticalDelta = MathF.Abs(delta.Y);

        if (verticalDelta > maxVerticalDelta)
            return false;

        delta.Y = 0f;
        float horizontalDistance = delta.Length();

        if (horizontalDistance < 0.1f)
            return verticalDelta <= 1.5f;

        float slopeRatio = verticalDelta / horizontalDistance;

        if (slopeRatio > maxSlopeRatio)
            return false;

        return true;
    }

    private static NavGraph GetOrLoadGraph(uint territoryId)
    {
        if (Graphs.TryGetValue(territoryId, out var graph))
            return graph;

        graph = LoadGraph(territoryId);
        Graphs[territoryId] = graph;

        return graph;
    }

    private static NavNode? GetNearestNodeExcluding(NavGraph graph, Vector3 position, float maxDistance, string? excludedId)
    {
        NavNode? best = null;
        float bestDistanceSq = maxDistance * maxDistance;

        foreach (var node in graph.Nodes)
        {
            if (node.Id == excludedId)
                continue;

            float distanceSq = Vector3.DistanceSquared(node.Position, position);

            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                best = node;
            }
        }

        return best;
    }
    public static void RecordPlayerPosition(uint territoryId, Vector3 position)
    {
        var graph = GetOrLoadGraph(territoryId);

        if (territoryId != LastTerritoryId)
        {
            LastRecordedPosition = null;
            LastMovementDirection = null;
            LastRecordedNodeId = null;
            LastTerritoryId = territoryId;
        }

        if (!IsValidNodePosition(position))
            return;

        if (LastRecordedPosition.HasValue)
        {
            float movedDistance = Vector3.Distance(LastRecordedPosition.Value, position);

            if (movedDistance > TeleportResetDistance)
            {
                LastRecordedPosition = null;
                LastMovementDirection = null;
                LastRecordedNodeId = null;
            }
        }

        if (LastRecordedPosition.HasValue)
        {
            Vector3 movement = position - LastRecordedPosition.Value;
            movement.Y = 0f;

            float movedDistance = movement.Length();

            if (movedDistance < CornerNodeSpacing)
                return;

            Vector3 currentDirection = Vector3.Normalize(movement);

            bool changedDirection = false;

            if (LastMovementDirection.HasValue)
            {
                float directionDot = Vector3.Dot(LastMovementDirection.Value, currentDirection);
                changedDirection = directionDot < 1.0f - DirectionChangeThreshold;
            }

            bool movedFarEnough = movedDistance >= NodeSpacing;

            if (!movedFarEnough && !changedDirection)
                return;

            LastMovementDirection = currentDirection;
        }

        var nearbyExistingNode = graph.GetNearestNode(position, NodeSpacing * 0.6f);

        if (nearbyExistingNode != null)
        {
            if (LastRecordedNodeId != null)
            {
                var previousNode = graph.GetNode(LastRecordedNodeId);

                if (previousNode != null && previousNode.Id != nearbyExistingNode.Id)
                {
                    LinkNodes(previousNode, nearbyExistingNode);
                    SaveGraph(territoryId, graph);
                }
            }

            LastRecordedPosition = nearbyExistingNode.Position;
            LastRecordedNodeId = nearbyExistingNode.Id;
            return;
        }

        string newId = $"{territoryId}_{graph.Nodes.Count + 1:D6}";

        var newNode = new NavNode
        {
            Id = newId,
            Position = position,
            Links = new List<string>()
        };

        if (LastRecordedNodeId != null)
        {
            var previousNode = graph.GetNode(LastRecordedNodeId);

            if (previousNode != null)
                LinkNodes(previousNode, newNode);
        }

        if (LastRecordedNodeId == null)
        {
            var bridgeNode = GetNearestNodeExcluding(
                graph,
                position,
                SessionAttachDistance,
                null
            );

            if (bridgeNode != null)
                LinkNodes(bridgeNode, newNode);
        }

        graph.AddNode(newNode);

        LastRecordedPosition = position;
        LastRecordedNodeId = newNode.Id;

        SaveGraph(territoryId, graph);
    }

    public static bool ExportGraph(uint territoryId, out string exportPath)
    {
        exportPath = GetExportPath(territoryId);

        try
        {
            PruneGraph(territoryId);

            var graph = GetOrLoadGraph(territoryId);

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);

            string json = JsonSerializer.Serialize(graph, JsonOptions);
            File.WriteAllText(exportPath, json);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to export AetherTrail graph for territory {territoryId}.");
            return false;
        }
    }

    public static bool ImportGraph(uint territoryId, out string importPath, out int importedNodes)
    {
        importPath = GetImportPath(territoryId);
        importedNodes = 0;

        if (!File.Exists(importPath))
            return false;

        try
        {
            string json = File.ReadAllText(importPath);
            var importedGraph = JsonSerializer.Deserialize<NavGraph>(json, JsonOptions);

            if (importedGraph == null)
                return false;

            var currentGraph = GetOrLoadGraph(territoryId);

            importedNodes = MergeGraph(currentGraph, importedGraph);

            SaveGraph(territoryId, currentGraph);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to import AetherTrail graph for territory {territoryId}.");
            return false;
        }
    }

    private static bool IsValidNodePosition(Vector3 position)
    {
        if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z))
            return false;

        if (float.IsInfinity(position.X) || float.IsInfinity(position.Y) || float.IsInfinity(position.Z))
            return false;

        if (MathF.Abs(position.X) > 5000f)
            return false;

        if (MathF.Abs(position.Y) > 2000f)
            return false;

        if (MathF.Abs(position.Z) > 5000f)
            return false;

        return true;
    }

    private static bool IsInsideKnownGraphBounds(NavGraph graph, Vector3 position, float padding = 150f)
    {
        if (graph.Nodes.Count < 5)
            return true;

        float minX = graph.Nodes.Min(n => n.Position.X) - padding;
        float maxX = graph.Nodes.Max(n => n.Position.X) + padding;

        float minY = graph.Nodes.Min(n => n.Position.Y) - padding;
        float maxY = graph.Nodes.Max(n => n.Position.Y) + padding;

        float minZ = graph.Nodes.Min(n => n.Position.Z) - padding;
        float maxZ = graph.Nodes.Max(n => n.Position.Z) + padding;

        return position.X >= minX &&
               position.X <= maxX &&
               position.Y >= minY &&
               position.Y <= maxY &&
               position.Z >= minZ &&
               position.Z <= maxZ;
    }

    private static int MergeGraph(NavGraph currentGraph, NavGraph importedGraph)
    {
        const float mergeDistance = 2.5f;

        Dictionary<string, string> idMap = new();
        int importedCount = 0;

        foreach (var importedNode in importedGraph.Nodes)
        {
            if (!IsValidNodePosition(importedNode.Position))
                continue;

            if (!IsInsideKnownGraphBounds(currentGraph, importedNode.Position))
                continue;

            var existingNode = currentGraph.GetNearestNode(importedNode.Position, mergeDistance);

            if (existingNode != null)
            {
                idMap[importedNode.Id] = existingNode.Id;
                continue;
            }

            string newId = $"imported_{Guid.NewGuid():N}";

            var newNode = new NavNode
            {
                Id = newId,
                Position = importedNode.Position,
                Links = new List<string>()
            };

            currentGraph.AddNode(newNode);
            idMap[importedNode.Id] = newId;
            importedCount++;
        }

        foreach (var importedNode in importedGraph.Nodes)
        {
            if (!idMap.TryGetValue(importedNode.Id, out string? mappedSourceId))
                continue;

            var sourceNode = currentGraph.GetNode(mappedSourceId);

            if (sourceNode == null)
                continue;

            foreach (string importedLinkId in importedNode.Links)
            {
                if (!idMap.TryGetValue(importedLinkId, out string? mappedTargetId))
                    continue;

                var targetNode = currentGraph.GetNode(mappedTargetId);

                if (targetNode == null || targetNode.Id == sourceNode.Id)
                    continue;

                LinkNodes(sourceNode, targetNode);
            }
        }

        return importedCount;
    }

    private static string GetExportPath(uint territoryId)
    {
        string folder = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Exports"
        );

        return Path.Combine(folder, $"{territoryId}.json");
    }

    private static string GetImportPath(uint territoryId)
    {
        string folder = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Imports"
        );

        return Path.Combine(folder, $"{territoryId}.json");
    }

    private static NavGraph LoadGraph(uint territoryId)
    {
        string path = GetGraphPath(territoryId);

        if (!File.Exists(path))
            return new NavGraph();

        try
        {
            string json = File.ReadAllText(path);
            var graph = JsonSerializer.Deserialize<NavGraph>(json, JsonOptions);
            return graph ?? new NavGraph();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to load AetherTrail graph for territory {territoryId}.");
            return new NavGraph();
        }
    }

    private static void SaveGraph(uint territoryId, NavGraph graph)
    {
        try
        {
            string path = GetGraphPath(territoryId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(graph, JsonOptions);

            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to save AetherTrail graph for territory {territoryId}.");
        }
    }

    private static string GetGraphPath(uint territoryId)
    {
        string folder = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Graphs"
        );

        return Path.Combine(folder, $"{territoryId}.json");
    }

    public static bool ExportSyncPacket(uint territoryId, out string path)
    {
        path = GetSyncOutPath(territoryId);

        try
        {
            PruneGraph(territoryId);

            var packet = new GraphSyncPacket
            {
                TerritoryId = territoryId,
                SenderId = Environment.MachineName,
                Graph = GetOrLoadGraph(territoryId)
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string json = JsonSerializer.Serialize(packet, JsonOptions);
            File.WriteAllText(path, json);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to export sync packet.");
            return false;
        }
    }

    public static bool PreviewSyncPacket(uint currentTerritoryId, out string path, out GraphSyncPreview preview)
    {
        path = GetSyncInPath(currentTerritoryId);
        preview = new GraphSyncPreview
        {
            Success = false,
            Message = "No packet found."
        };

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            var packet = JsonSerializer.Deserialize<GraphSyncPacket>(json, JsonOptions);

            if (packet == null)
            {
                preview = new GraphSyncPreview
                {
                    Success = false,
                    Message = "Packet could not be read."
                };
                return false;
            }

            if (packet.TerritoryId != currentTerritoryId)
            {
                preview = new GraphSyncPreview
                {
                    Success = false,
                    Message = $"Wrong territory. Packet={packet.TerritoryId}, Current={currentTerritoryId}",
                    TerritoryId = packet.TerritoryId,
                    SenderId = packet.SenderId,
                    PacketNodes = packet.Graph.Nodes.Count
                };
                return false;
            }

            var currentGraph = GetOrLoadGraph(currentTerritoryId);

            int newNodes = 0;
            int duplicates = 0;
            int invalid = 0;
            int outOfBounds = 0;

            const float mergeDistance = 2.5f;

            foreach (var node in packet.Graph.Nodes)
            {
                if (!IsValidNodePosition(node.Position))
                {
                    invalid++;
                    continue;
                }

                if (!IsInsideKnownGraphBounds(currentGraph, node.Position))
                {
                    outOfBounds++;
                    continue;
                }

                var existing = currentGraph.GetNearestNode(node.Position, mergeDistance);

                if (existing != null)
                    duplicates++;
                else
                    newNodes++;
            }

            preview = new GraphSyncPreview
            {
                Success = true,
                Message = "Preview ready.",
                TerritoryId = packet.TerritoryId,
                SenderId = packet.SenderId,
                PacketNodes = packet.Graph.Nodes.Count,
                NewNodes = newNodes,
                DuplicateNodes = duplicates,
                RejectedInvalidNodes = invalid,
                RejectedOutOfBoundsNodes = outOfBounds
            };

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to preview sync packet.");

            preview = new GraphSyncPreview
            {
                Success = false,
                Message = "Preview failed."
            };

            return false;
        }
    }

    public static bool ImportSyncPacket(uint currentTerritoryId, out string path, out int importedNodes)
    {
        path = GetSyncInPath(currentTerritoryId);
        importedNodes = 0;

        if (!File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            var packet = JsonSerializer.Deserialize<GraphSyncPacket>(json, JsonOptions);

            if (packet == null)
                return false;

            if (packet.TerritoryId != currentTerritoryId)
                return false;

            var currentGraph = GetOrLoadGraph(currentTerritoryId);

            importedNodes = MergeGraph(currentGraph, packet.Graph);

            SaveGraph(currentTerritoryId, currentGraph);

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to import sync packet.");
            return false;
        }
    }

    private static string GetSyncOutPath(uint territoryId)
    {
        string folder = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "SyncOut");
        return Path.Combine(folder, $"{territoryId}.json");
    }

    private static string GetSyncInPath(uint territoryId)
    {
        string folder = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "SyncIn");
        return Path.Combine(folder, $"{territoryId}.json");
    }

    public static GraphSyncPacket CreateSyncPacket(uint territoryId)
    {
        PruneGraph(territoryId);

        return new GraphSyncPacket
        {
            TerritoryId = territoryId,
            SenderId = Environment.MachineName,
            Graph = GetOrLoadGraph(territoryId)
        };
    }

    public static int ImportSyncPacket(GraphSyncPacket packet)
    {
        var graph = GetOrLoadGraph(packet.TerritoryId);

        int imported = MergeGraph(graph, packet.Graph);

        SaveGraph(packet.TerritoryId, graph);

        return imported;
    }
}
