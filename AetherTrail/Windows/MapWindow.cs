using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Files;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail.Windows;

public sealed class MapWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private float zoom = 1.0f;
    private Vector2 pan = Vector2.Zero;

    private bool showGround = true;
    private bool showFlight = true;
    private bool showLinks = true;
    private bool showNodes = true;
    private bool showActiveRoute = true;
    private MapDisplayMode displayMode = MapDisplayMode.Normal;

    private IDalamudTextureWrap? mapTexture;
    private uint loadedMapTerritoryId;
    private uint failedMapTerritoryId;

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
        this.mapTexture?.Dispose();
        this.mapTexture = null;
    }

    public override void Draw()
    {
        UiOcclusionService.AddRect(
            ImGui.GetWindowPos(),
            ImGui.GetWindowPos() + ImGui.GetWindowSize()
        );

        // existing draw code...
    
        uint territoryId = Plugin.ClientState.TerritoryType;
        var graph = NavigationManager.GetGraph(territoryId);

        ImGui.Text($"Territory: {territoryId}");
        ImGui.Text($"Nodes: {graph.Nodes.Count}");
        ImGui.SliderFloat("Zoom", ref this.zoom, 0.1f, 10.0f);

        ImGui.Checkbox("Ground", ref this.showGround);
        ImGui.SameLine();
        ImGui.Checkbox("Flight", ref this.showFlight);
        ImGui.SameLine();
        ImGui.Checkbox("Links", ref this.showLinks);
        ImGui.SameLine();
        ImGui.Checkbox("Nodes", ref this.showNodes);
        ImGui.SameLine();
        ImGui.Checkbox("Route", ref this.showActiveRoute);
        ImGui.SameLine();
        ImGui.Text("Display");
        ImGui.SameLine();

        int displayModeValue = (int)this.displayMode;

        if (ImGui.Combo(
                "##MapDisplayMode",
                ref displayModeValue,
                "Normal\0Confidence\0Density\0"))
        {
            this.displayMode = (MapDisplayMode)displayModeValue;
        }

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
        drawList.PushClipRect(canvasPos, canvasPos + canvasSize, true);

        var texture = GetCurrentMapTexture(territoryId);

        if (texture != null)
        {
            Vector2 textureMin = MapNormalizedToCanvas(Vector2.Zero, canvasPos, canvasSize);
            Vector2 textureMax = MapNormalizedToCanvas(Vector2.One, canvasPos, canvasSize);

            drawList.AddImage(
                texture.Handle,
                textureMin,
                textureMax,
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.65f))
            );
        }

        ImGui.InvisibleButton("AetherTrailMapCanvas", canvasSize);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            this.pan += ImGui.GetIO().MouseDelta;

        var visibleNodes = graph.GetSnapshot()
            .Where(ShouldShowNode)
            .ToList();

        if (visibleNodes.Count > 0)
        {
            DrawGraph(drawList, visibleNodes, canvasPos, canvasSize);
            DrawActiveRoute(drawList, canvasPos, canvasSize);
            DrawPlayer(drawList, canvasPos, canvasSize);
        }

        drawList.PopClipRect();
        drawList.AddRect(canvasPos, canvasPos + canvasSize, borderColor);
    }

    private void DrawGraph(
    ImDrawListPtr drawList,
    System.Collections.Generic.List<NavNodeSnapshot> visibleNodes,
    Vector2 canvasPos,
    Vector2 canvasSize)
    {
        var nodesById = new Dictionary<string, NavNodeSnapshot>();

        foreach (var node in visibleNodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
                continue;

            if (!nodesById.ContainsKey(node.Id))
                nodesById[node.Id] = node;
        }

        Dictionary<string, int> densityByNodeId = this.displayMode == MapDisplayMode.Density
            ? BuildDensityLookup(visibleNodes, 18.0f)
            : new Dictionary<string, int>();

        if (this.showLinks)
        {
            foreach (var node in visibleNodes)
            {
                Vector2 a = ToCanvas(node.Position, canvasPos, canvasSize);

                if (!IsInsideCanvas(a, canvasPos, canvasSize))
                    continue;

                foreach (string linkId in node.Links)
                {
                    if (!nodesById.TryGetValue(linkId, out var linked))
                        continue;

                    Vector2 b = ToCanvas(linked.Position, canvasPos, canvasSize);

                    if (!IsInsideCanvas(b, canvasPos, canvasSize))
                        continue;

                    int confidence = node.LinkConfidence.TryGetValue(linkId, out int value)
                        ? value
                        : 1;

                    uint linkColor;
                    float thickness;

                    switch (this.displayMode)
                    {
                        case MapDisplayMode.Confidence:
                            linkColor = GetConfidenceColor(confidence, 0.65f);
                            thickness = Math.Clamp(1.0f + confidence / 35.0f, 1.0f, 4.0f);
                            break;

                        case MapDisplayMode.Density:
                            {
                                int aDensity = densityByNodeId.TryGetValue(node.Id, out int aValue) ? aValue : 0;
                                int bDensity = densityByNodeId.TryGetValue(linked.Id, out int bValue) ? bValue : 0;
                                int density = Math.Max(aDensity, bDensity);

                                linkColor = GetDensityColor(density, 0.65f);
                                thickness = Math.Clamp(1.0f + density / 8.0f, 1.0f, 4.5f);
                                break;
                            }

                        default:
                            linkColor = node.TraversalMode == NavTraversalMode.Flight
                                ? GetFlightColor(0.45f)
                                : GetConfidenceColor(confidence, 0.45f);

                            thickness = 1.5f;
                            break;
                    }

                    drawList.AddLine(a, b, linkColor, thickness);
                }
            }
        }

        if (!this.showNodes)
            return;

        foreach (var node in visibleNodes)
        {
            Vector2 p = ToCanvas(node.Position, canvasPos, canvasSize);

            if (!IsInsideCanvas(p, canvasPos, canvasSize))
                continue;

            int nodeConfidence = NavConfidence.GetMedianConfidence(node);

            uint nodeColor;

            switch (this.displayMode)
            {
                case MapDisplayMode.Confidence:
                    nodeColor = GetConfidenceColor(nodeConfidence, 0.95f);
                    break;

                case MapDisplayMode.Density:
                    {
                        int density = densityByNodeId.TryGetValue(node.Id, out int value) ? value : 0;
                        nodeColor = GetDensityColor(density, 0.95f);
                        break;
                    }

                default:
                    nodeColor = node.TraversalMode == NavTraversalMode.Flight
                        ? GetFlightColor(0.9f)
                        : GetConfidenceColor(nodeConfidence, 0.9f);
                    break;
            }

            float radius = node.TraversalMode == NavTraversalMode.Flight
                ? 3.5f
                : 3.0f;

            drawList.AddCircleFilled(p, radius, nodeColor);
        }
    }

    private void DrawActiveRoute(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        if (!this.showActiveRoute)
            return;

        var route = this.plugin.TrailRenderer.GetCurrentDisplayPathSnapshot();

        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector2 a = ToCanvas(route[i].Position, canvasPos, canvasSize);
            Vector2 b = ToCanvas(route[i + 1].Position, canvasPos, canvasSize);

            if (!IsInsideCanvas(a, canvasPos, canvasSize) && !IsInsideCanvas(b, canvasPos, canvasSize))
                continue;

            uint routeColor = ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.15f, 0.85f, 1.0f, 0.95f)
            );

            drawList.AddLine(a, b, routeColor, 3.0f);
        }
    }

    private void DrawPlayer(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        Vector2 playerPoint = ToCanvas(player.Position, canvasPos, canvasSize);

        if (!IsInsideCanvas(playerPoint, canvasPos, canvasSize))
            return;

        uint playerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 1f, 1f));
        drawList.AddCircleFilled(playerPoint, 6f, playerColor);
    }

    private Vector2 ToCanvas(Vector3 world, Vector2 canvasPos, Vector2 canvasSize)
    {
        return MapNormalizedToCanvas(
            WorldToMapNormalized(world),
            canvasPos,
            canvasSize
        );
    }

    private Vector2 MapNormalizedToCanvas(Vector2 normalized, Vector2 canvasPos, Vector2 canvasSize)
    {
        Vector2 local = normalized * canvasSize;
        local -= canvasSize * 0.5f;
        local *= this.zoom;

        return canvasPos + canvasSize * 0.5f + local + this.pan;
    }

    private unsafe Vector2 WorldToMapNormalized(Vector3 world)
    {
        if (!TryGetMapTransform(out float sizeFactor, out float offsetX, out float offsetY))
            return new Vector2(0.5f, 0.5f);

        float mapX = ((world.X + offsetX) * sizeFactor / 100f) + 1024f;
        float mapY = ((world.Z + offsetY) * sizeFactor / 100f) + 1024f;

        return new Vector2(
            mapX / 2048f,
            mapY / 2048f
        );
    }

    private unsafe bool TryGetMapTransform(out float sizeFactor, out float offsetX, out float offsetY)
    {
        sizeFactor = 100f;
        offsetX = 0f;
        offsetY = 0f;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
            return false;

        sizeFactor = agentMap->CurrentMapSizeFactor;

        if (sizeFactor <= 0)
            sizeFactor = agentMap->SelectedMapSizeFactor;

        offsetX = agentMap->CurrentOffsetX;
        offsetY = agentMap->CurrentOffsetY;

        if (offsetX == 0 && offsetY == 0)
        {
            offsetX = agentMap->SelectedOffsetX;
            offsetY = agentMap->SelectedOffsetY;
        }

        return sizeFactor > 0;
    }

    private static bool IsInsideCanvas(Vector2 point, Vector2 canvasPos, Vector2 canvasSize)
    {
        return point.X >= canvasPos.X &&
               point.X <= canvasPos.X + canvasSize.X &&
               point.Y >= canvasPos.Y &&
               point.Y <= canvasPos.Y + canvasSize.Y;
    }

    private bool ShouldShowNode(NavNodeSnapshot node)
    {
        return node.TraversalMode switch
        {
            NavTraversalMode.Ground => this.showGround,
            NavTraversalMode.Flight => this.showFlight,
            _ => true
        };
    }

    private static Dictionary<string, int> BuildDensityLookup(
    List<NavNodeSnapshot> nodes,
    float radius)
    {
        Dictionary<string, int> densityByNodeId = new();

        float radiusSq = radius * radius;

        foreach (var node in nodes)
        {
            int count = 0;

            foreach (var other in nodes)
            {
                if (node.Id == other.Id)
                    continue;

                if (node.TraversalMode != other.TraversalMode)
                    continue;

                float distanceSq = Vector3.DistanceSquared(node.Position, other.Position);

                if (distanceSq <= radiusSq)
                    count++;
            }

            densityByNodeId[node.Id] = count;
        }

        return densityByNodeId;
    }

    private static uint GetHeatmapColor(int confidence, float alpha)
    {
        confidence = Math.Clamp(confidence, 1, 20);

        float t = (confidence - 1) / 19f;

        float red;
        float green;
        float blue;

        if (t < 0.5f)
        {
            float local = t / 0.5f;

            red = local;
            green = local * 0.65f;
            blue = 1f - local;
        }
        else
        {
            float local = (t - 0.5f) / 0.5f;

            red = 1f;
            green = 0.65f + local * 0.35f;
            blue = 0f;
        }

        return ImGui.ColorConvertFloat4ToU32(new Vector4(red, green, blue, alpha));
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

    private static uint GetDensityColor(int density, float alpha)
    {
        density = Math.Clamp(density, 0, 30);

        float t = density / 30f;

        Vector4 color;

        if (t < 0.33f)
        {
            float local = t / 0.33f;

            color = Lerp(
                new Vector4(0.1f, 0.35f, 1.0f, alpha),   // blue
                new Vector4(0.1f, 1.0f, 0.35f, alpha),   // green
                local
            );
        }
        else if (t < 0.66f)
        {
            float local = (t - 0.33f) / 0.33f;

            color = Lerp(
                new Vector4(0.1f, 1.0f, 0.35f, alpha),   // green
                new Vector4(1.0f, 1.0f, 0.0f, alpha),    // yellow
                local
            );
        }
        else
        {
            float local = (t - 0.66f) / 0.34f;

            color = Lerp(
                new Vector4(1.0f, 1.0f, 0.0f, alpha),    // yellow
                new Vector4(1.0f, 0.1f, 0.0f, alpha),    // red
                local
            );
        }

        return ImGui.ColorConvertFloat4ToU32(color);
    }

    private static uint GetFlightColor(float alpha)
    {
        return ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.75f, 1.0f, alpha));
    }

    private unsafe IDalamudTextureWrap? GetCurrentMapTexture(uint territoryId)
    {
        if (this.mapTexture != null && this.loadedMapTerritoryId == territoryId)
            return this.mapTexture;

        if (this.failedMapTerritoryId == territoryId)
            return null;

        this.mapTexture?.Dispose();
        this.mapTexture = null;
        this.loadedMapTerritoryId = territoryId;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
        {
            this.failedMapTerritoryId = territoryId;
            return null;
        }

        string path = agentMap->CurrentMapPath.ToString();

        if (string.IsNullOrWhiteSpace(path))
            path = agentMap->SelectedMapPath.ToString();

        if (string.IsNullOrWhiteSpace(path))
            path = agentMap->CurrentMapBgPath.ToString();

        if (string.IsNullOrWhiteSpace(path))
            path = agentMap->SelectedMapBgPath.ToString();

        if (string.IsNullOrWhiteSpace(path))
        {
            this.failedMapTerritoryId = territoryId;
            return null;
        }

        string basePath = path.EndsWith(".tex", StringComparison.OrdinalIgnoreCase)
            ? path[..^4]
            : path;

        string[] texturePaths =
        {
            $"{basePath}.tex",
            $"{basePath.Replace("_m", "m_m")}.tex",
            $"{basePath.Replace("_m", "_s")}.tex"
        };

        foreach (string texturePath in texturePaths.Distinct())
        {
            try
            {
                var texFile = Plugin.DataManager.GetFile<TexFile>(texturePath);

                if (texFile == null)
                    continue;

                this.mapTexture = Plugin.TextureProvider.CreateFromTexFile(texFile);

                if (this.mapTexture != null)
                {
                    this.failedMapTerritoryId = 0;
                    return this.mapTexture;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, $"Failed to load map texture candidate: {texturePath}");
            }
        }

        this.failedMapTerritoryId = territoryId;
        return null;
    }
}
