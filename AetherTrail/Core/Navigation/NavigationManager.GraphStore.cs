using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    private static void MarkGraphDirty(uint territoryId)
    {
        DirtyGraphs.Add(territoryId);
    }

    public static void FlushDirtyGraphsImmediately()
    {
        foreach (uint territoryId in DirtyGraphs.ToList())
        {
            if (!Graphs.TryGetValue(territoryId, out var graph))
                continue;

            SaveGraph(territoryId, graph);
        }

        DirtyGraphs.Clear();
        LastFlushTime = DateTime.UtcNow;
    }

    public static void FlushDirtyGraphs()
    {
        double elapsed = (DateTime.UtcNow - LastFlushTime).TotalSeconds;

        if (elapsed < SaveFlushIntervalSeconds)
            return;

        LastFlushTime = DateTime.UtcNow;

        foreach (uint territoryId in DirtyGraphs.ToList())
        {
            if (!Graphs.TryGetValue(territoryId, out var graph))
                continue;

            SaveGraph(territoryId, graph);
        }

        DirtyGraphs.Clear();
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

    private static NavGraph GetOrLoadGraph(uint territoryId)
    {
        if (Graphs.TryGetValue(territoryId, out var graph))
            return graph;

        graph = LoadGraph(territoryId);
        Graphs[territoryId] = graph;

        return graph;
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
}
