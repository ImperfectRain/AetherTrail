using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherTrail;

public class GraphDebugRenderer
{
    public bool Enabled;

    

    public void Draw()
    {
        if (!Enabled)
            return;

        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        uint territoryId = Plugin.ClientState.TerritoryType;
        var graph = NavigationManager.GetGraph(territoryId);

        var drawList = ImGui.GetBackgroundDrawList();
        var playerPos = player.Position;

        foreach (var node in graph.Nodes)
        {
            if (Vector3.Distance(playerPos, node.Position) > Plugin.Instance.Configuration.GraphDebugDrawDistance)
                continue;

            Vector3 nodeWorld = node.Position + new Vector3(0f, 0.65f, 0f);

            if (!Plugin.GameGui.WorldToScreen(nodeWorld, out Vector2 nodeScreen))
                continue;

            bool isolated = node.Links.Count == 0;

            uint nodeColor = ImGui.ColorConvertFloat4ToU32(
                isolated
                    ? new Vector4(1.0f, 0.2f, 0.2f, 0.9f)
                    : new Vector4(0.2f, 1.0f, 0.3f, 0.9f)
            );

            drawList.AddCircleFilled(nodeScreen, isolated ? 6f : 4f, nodeColor);

            foreach (string linkId in node.Links)
            {
                var linkedNode = graph.GetNode(linkId);

                if (linkedNode == null)
                    continue;

                if (Vector3.Distance(playerPos, linkedNode.Position) > Plugin.Instance.Configuration.GraphDebugDrawDistance)
                    continue;

                Vector3 linkedWorld = linkedNode.Position + new Vector3(0f, 0.65f, 0f);

                if (!Plugin.GameGui.WorldToScreen(linkedWorld, out Vector2 linkedScreen))
                    continue;

                uint linkColor = ImGui.ColorConvertFloat4ToU32(
                    new Vector4(0.2f, 1.0f, 0.3f, 0.35f)
                );

                drawList.AddLine(nodeScreen, linkedScreen, linkColor, 2f);
            }
        }
    }
}
