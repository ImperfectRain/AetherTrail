using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

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

    private static int MergeGraph(NavGraph currentGraph, NavGraph importedGraph, bool allowOutOfBounds = false)
    {
        int rejectedInvalid = 0;
        int rejectedOutOfBounds = 0;
        int duplicateNodes = 0;
        int addedNodes = 0;

        const float mergeDistance = 2.5f;

        Dictionary<string, string> idMap = new();
        int importedCount = 0;

        foreach (var importedNode in importedGraph.Nodes)
        {
            if (!IsValidNodePosition(importedNode.Position))
            {
                rejectedInvalid++;
                continue;
            }

            if (!allowOutOfBounds && !IsInsideKnownGraphBounds(currentGraph, importedNode.Position))
            {
                rejectedOutOfBounds++;
                continue;
            }

            var existingNode = currentGraph.GetNearestNode(importedNode.Position, mergeDistance);

            if (existingNode != null)
            {
                idMap[importedNode.Id] = existingNode.Id;
                duplicateNodes++;
                continue;
            }

            string newId = $"imported_{Guid.NewGuid():N}";

            var newNode = new NavNode
            {
                Id = newId,
                Position = importedNode.Position,
                TraversalMode = importedNode.TraversalMode,
                Links = new List<string>()
            };

            currentGraph.AddNode(newNode);
            idMap[importedNode.Id] = newId;
            importedCount++;
            addedNodes++;
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

                LinkNodes(currentGraph, sourceNode, targetNode, increaseConfidence: false);

                int importedConfidence = importedNode.LinkConfidence.TryGetValue(importedLinkId, out int value)
                    ? value
                    : NavConfidence.ImportedConfidence();

                NavConfidence.MergeLinkConfidence(sourceNode, targetNode, importedConfidence);
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
}
