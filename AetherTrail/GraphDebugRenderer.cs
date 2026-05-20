using System;
using System.Linq;
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

            int averageConfidence = 1;

            if (node.LinkConfidence.Count > 0)
                averageConfidence = (int)Math.Round(node.LinkConfidence.Values.Average());

            uint nodeColor = isolated
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.2f, 0.2f, 0.9f))
                : GetConfidenceColor(averageConfidence, 0.9f);

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

                int linkConfidence = node.LinkConfidence.TryGetValue(linkId, out int value)
                    ? value
                    : 1;

                uint linkColor = GetConfidenceColor(linkConfidence, 0.35f);

                drawList.AddLine(nodeScreen, linkedScreen, linkColor, 2f);
            }
        }
    }

    private static uint GetConfidenceColor(int confidence, float alpha)
    {
        confidence = Math.Clamp(confidence, 1, 6);

        float t = (confidence - 1) / 5f;
        float red = 1f - (0.8f * t);
        float green = 1f;
        float blue = 0f;

        return ImGui.ColorConvertFloat4ToU32(
            new Vector4(red, green, blue, alpha)
        );
    }
}
