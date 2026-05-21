using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    public static bool ExportSyncPacket(uint territoryId, out string path)
    {
        path = GetSyncOutPath(territoryId);

        try
        {
            PruneGraph(territoryId);

            var packet = new GraphSyncPacket
            {
                TerritoryId = territoryId,
                SenderId = GetSyncSenderId(),
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
            SenderId = GetSyncSenderId(),
            Graph = GetOrLoadGraph(territoryId)
        };
    }

    private static string GetSyncSenderId()
    {
        var config = Plugin.Instance.Configuration;

        if (string.IsNullOrWhiteSpace(config.SyncClientId))
        {
            config.SyncClientId = Guid.NewGuid().ToString("N");
            config.Save();
        }

        return config.SyncClientId;
    }

    public static int ImportSyncPacket(GraphSyncPacket packet)
    {
        var graph = GetOrLoadGraph(packet.TerritoryId);

        int imported = MergeGraph(
            graph,
            packet.Graph,
            allowOutOfBounds: true
        );

        SaveGraph(packet.TerritoryId, graph);

        return imported;
    }
}
