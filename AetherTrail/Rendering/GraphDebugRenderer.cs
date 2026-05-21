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
        confidence = NavConfidence.Clamp(confidence);

        Vector4 color;

        if (confidence >= NavConfidence.Locked)
        {
            color = new Vector4(0.15f, 0.45f, 1.0f, alpha);
        }
        else if (confidence >= NavConfidence.Trusted)
        {
            float t = (confidence - NavConfidence.Trusted) /
                      (float)(NavConfidence.Locked - NavConfidence.Trusted);

            color = Lerp(
                new Vector4(0.15f, 1.0f, 0.25f, alpha),
                new Vector4(0.15f, 0.45f, 1.0f, alpha),
                t
            );
        }
        else if (confidence >= NavConfidence.Imported)
        {
            float t = (confidence - NavConfidence.Imported) /
                      (float)(NavConfidence.Trusted - NavConfidence.Imported);

            color = Lerp(
                new Vector4(1.0f, 1.0f, 0.0f, alpha),
                new Vector4(0.15f, 1.0f, 0.25f, alpha),
                t
            );
        }
        else if (confidence >= NavConfidence.Weak)
        {
            float t = (confidence - NavConfidence.Weak) /
                      (float)(NavConfidence.Imported - NavConfidence.Weak);

            color = Lerp(
                new Vector4(1.0f, 0.45f, 0.0f, alpha),
                new Vector4(1.0f, 1.0f, 0.0f, alpha),
                t
            );
        }
        else
        {
            float t = confidence / (float)NavConfidence.Weak;

            color = Lerp(
                new Vector4(1.0f, 0.0f, 0.0f, alpha),
                new Vector4(1.0f, 0.45f, 0.0f, alpha),
                t
            );
        }

        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private static Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return a + ((b - a) * t);
    }
}
