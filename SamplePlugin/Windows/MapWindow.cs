using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace AetherTrail.Windows;

public sealed class MapWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private float zoom = 1.0f;
    private Vector2 pan = Vector2.Zero;

    public MapWindow(Plugin plugin)
        : base("AetherTrail Map###AetherTrailMap")
    {
        this.plugin = plugin;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        uint territoryId = Plugin.ClientState.TerritoryType;
        var graph = NavigationManager.GetGraph(territoryId);

        ImGui.Text($"Territory: {territoryId}");
        ImGui.Text($"Nodes: {graph.Nodes.Count}");
        ImGui.SliderFloat("Zoom", ref this.zoom, 0.1f, 10.0f);

        if (ImGui.Button("Reset View"))
        {
            this.zoom = 1.0f;
            this.pan = Vector2.Zero;
        }

        ImGui.Separator();

        Vector2 canvasPos = ImGui.GetCursorScreenPos();
        Vector2 canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X < 50f)
            canvasSize.X = 50f;

        if (canvasSize.Y < 50f)
            canvasSize.Y = 50f;

        var drawList = ImGui.GetWindowDrawList();

        uint bgColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.05f, 1f));
        uint borderColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));

        drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, bgColor);
        drawList.AddRect(canvasPos, canvasPos + canvasSize, borderColor);

        ImGui.InvisibleButton("AetherTrailMapCanvas", canvasSize);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            this.pan += ImGui.GetIO().MouseDelta;

        if (graph.Nodes.Count == 0)
            return;

        float minX = graph.Nodes.Min(n => n.Position.X);
        float maxX = graph.Nodes.Max(n => n.Position.X);
        float minZ = graph.Nodes.Min(n => n.Position.Z);
        float maxZ = graph.Nodes.Max(n => n.Position.Z);

        float graphWidth = MathF.Max(maxX - minX, 1f);
        float graphHeight = MathF.Max(maxZ - minZ, 1f);

        float baseScale = MathF.Min(
            canvasSize.X / graphWidth,
            canvasSize.Y / graphHeight
        ) * 0.85f;

        float scale = baseScale * this.zoom;

        Vector2 center = canvasPos + canvasSize * 0.5f + this.pan;

        Vector2 ToCanvas(Vector3 world)
        {
            float x = (world.X - ((minX + maxX) * 0.5f)) * scale;
            float y = (world.Z - ((minZ + maxZ) * 0.5f)) * scale;

            return center + new Vector2(x, y);
        }

        foreach (var node in graph.Nodes)
        {
            Vector2 a = ToCanvas(node.Position);

            foreach (string linkId in node.Links)
            {
                var linked = graph.GetNode(linkId);

                if (linked == null)
                    continue;

                Vector2 b = ToCanvas(linked.Position);

                int confidence = node.LinkConfidence.TryGetValue(linkId, out int value)
                    ? value
                    : 1;

                uint linkColor = GetConfidenceColor(confidence, 0.45f);

                drawList.AddLine(a, b, linkColor, 1.5f);
            }
        }

        foreach (var node in graph.Nodes)
        {
            Vector2 p = ToCanvas(node.Position);

            int averageConfidence = 1;

            if (node.LinkConfidence.Count > 0)
                averageConfidence = (int)MathF.Round((float)node.LinkConfidence.Values.Average());

            uint nodeColor = GetConfidenceColor(averageConfidence, 0.9f);

            drawList.AddCircleFilled(p, 3.0f, nodeColor);
        }

        var player = Plugin.ObjectTable.LocalPlayer;

        if (player != null)
        {
            Vector2 playerPoint = ToCanvas(player.Position);
            uint playerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 1f, 1f));
            drawList.AddCircleFilled(playerPoint, 6f, playerColor);
        }
    }

    private static uint GetConfidenceColor(int confidence, float alpha)
    {
        confidence = Math.Clamp(confidence, 1, 6);

        float t = (confidence - 1) / 5f;

        float red = 1f - (0.8f * t);
        float green = 1f;
        float blue = 0f;

        return ImGui.ColorConvertFloat4ToU32(new Vector4(red, green, blue, alpha));
    }
}
