using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail.Windows;

public sealed class MapWindow : Window, IDisposable
{
    private const float BaseMapDisplaySize = 2048f;

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
    private string loadedTexturePath = string.Empty;
    private string failedTexturePath = string.Empty;

    private int cachedDensitySignature;
    private Dictionary<string, int> cachedDensityByNodeId = new();

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

        uint playerTerritoryId = Plugin.ClientState.TerritoryType;
        bool hasMapTransform = MapTransformService.TryGetCurrent(out var mapTransform);
        uint displayTerritoryId = hasMapTransform && mapTransform.TerritoryId != 0
            ? mapTransform.TerritoryId
            : playerTerritoryId;
        var graph = NavigationManager.GetGraph(displayTerritoryId);

        ImGui.Text($"Territory: {displayTerritoryId}");
        if (hasMapTransform)
        {
            ImGui.SameLine();
            ImGui.Text($"Map: {mapTransform.MapId}");
        }

        ImGui.Text($"Nodes: {graph.Nodes.Count}");
        ImGui.SameLine();
        ImGui.Text($"Transitions: {NavigationManager.GetTransitionMarkerCount(displayTerritoryId)}");
        ImGui.SliderFloat("Zoom", ref this.zoom, 0.1f, 20.0f);

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

        bool resetView = ImGui.Button("Reset View");
        ImGui.SameLine();
        bool centerPlayer = ImGui.Button("Center Player");
        ImGui.SameLine();
        bool fitGraph = ImGui.Button("Fit Graph");

        if (resetView)
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

        var visibleNodes = graph.GetSnapshot()
            .Where(ShouldShowNode)
            .ToList();

        if (hasMapTransform)
        {
            if (centerPlayer)
                CenterOnPlayer(mapTransform, canvasSize);

            if (fitGraph)
                FitGraphToCanvas(visibleNodes, mapTransform, canvasSize);
        }

        var texture = hasMapTransform
            ? GetCurrentMapTexture(mapTransform)
            : null;

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

        if (ImGui.IsItemHovered())
            ApplyMouseWheelZoom(canvasPos, canvasSize);

        if (hasMapTransform)
        {
            if (visibleNodes.Count > 0)
                DrawGraph(drawList, visibleNodes, mapTransform, canvasPos, canvasSize);

            if (displayTerritoryId == playerTerritoryId)
            {
                DrawActiveRoute(drawList, mapTransform, canvasPos, canvasSize);
                DrawPlayer(drawList, mapTransform, canvasPos, canvasSize);
                DrawPartyPresenceDots(drawList, mapTransform, canvasPos, canvasSize);
            }

            DrawTransitionMarkers(drawList, displayTerritoryId, mapTransform, canvasPos, canvasSize);
        }

        drawList.PopClipRect();
        drawList.AddRect(canvasPos, canvasPos + canvasSize, borderColor);
    }

    private void DrawGraph(
    ImDrawListPtr drawList,
    List<NavNodeSnapshot> visibleNodes,
    MapTransformSnapshot transform,
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
            ? GetDensityLookup(visibleNodes, 18.0f)
            : new Dictionary<string, int>();

        if (this.showLinks)
        {
            foreach (var node in visibleNodes)
            {
                Vector2 a = ToCanvas(node.Position, transform, canvasPos, canvasSize);

                foreach (string linkId in node.Links)
                {
                    if (!nodesById.TryGetValue(linkId, out var linked))
                        continue;

                    Vector2 b = ToCanvas(linked.Position, transform, canvasPos, canvasSize);

                    if (!IsSegmentVisible(a, b, canvasPos, canvasSize))
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
            Vector2 p = ToCanvas(node.Position, transform, canvasPos, canvasSize);

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

    private void DrawActiveRoute(
        ImDrawListPtr drawList,
        MapTransformSnapshot transform,
        Vector2 canvasPos,
        Vector2 canvasSize)
    {
        if (!this.showActiveRoute)
            return;

        var route = this.plugin.TrailRenderer.GetCurrentDisplayPathSnapshot();

        for (int i = 0; i < route.Count - 1; i++)
        {
            Vector2 a = ToCanvas(route[i].Position, transform, canvasPos, canvasSize);
            Vector2 b = ToCanvas(route[i + 1].Position, transform, canvasPos, canvasSize);

            if (!IsSegmentVisible(a, b, canvasPos, canvasSize))
                continue;

            uint routeColor = ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.15f, 0.85f, 1.0f, 0.95f)
            );

            drawList.AddLine(a, b, routeColor, 3.0f);
        }
    }

    private void DrawPlayer(
        ImDrawListPtr drawList,
        MapTransformSnapshot transform,
        Vector2 canvasPos,
        Vector2 canvasSize)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        Vector2 playerPoint = ToCanvas(player.Position, transform, canvasPos, canvasSize);

        if (!IsInsideCanvas(playerPoint, canvasPos, canvasSize))
            return;

        uint playerColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.6f, 1f, 1f));
        drawList.AddCircleFilled(playerPoint, 6f, playerColor);
    }

    private void DrawTransitionMarkers(
        ImDrawListPtr drawList,
        uint territoryId,
        MapTransformSnapshot transform,
        Vector2 canvasPos,
        Vector2 canvasSize)
    {
        var markers = NavigationManager.GetTransitionMapMarkers(territoryId);

        if (markers.Count == 0)
            return;

        Vector2 mouse = ImGui.GetMousePos();
        TerritoryTransitionMapMarker? hovered = null;
        uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.95f));
        uint exitColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.62f, 0.15f, 0.95f));
        uint entryColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.95f, 0.65f, 0.95f));

        foreach (var marker in markers)
        {
            Vector2 point = ToCanvas(marker.Position, transform, canvasPos, canvasSize);

            if (!IsInsideCanvas(point, canvasPos, canvasSize))
                continue;

            drawList.AddCircleFilled(point, 9.0f, outlineColor);
            drawList.AddCircleFilled(point, 6.75f, marker.IsSource ? exitColor : entryColor);
            drawList.AddCircle(point, 10.5f, marker.IsSource ? exitColor : entryColor, 16, 2.0f);

            if (IsInsideCanvas(mouse, canvasPos, canvasSize) &&
                Vector2.DistanceSquared(mouse, point) <= 144f)
            {
                hovered = marker;
            }
        }

        if (hovered == null)
            return;

        ImGui.BeginTooltip();
        ImGui.Text(hovered.IsSource ? "Territory exit" : "Territory entry");
        ImGui.Text($"{GetTerritoryLabel(hovered.SourceTerritoryId)} -> {GetTerritoryLabel(hovered.TargetTerritoryId)}");
        ImGui.Text($"Observed: {Math.Max(1, hovered.Observations)}");
        ImGui.EndTooltip();
    }

    private void DrawPartyPresenceDots(
        ImDrawListPtr drawList,
        MapTransformSnapshot transform,
        Vector2 canvasPos,
        Vector2 canvasSize)
    {
        var config = this.plugin.Configuration;

        if (!config.NetworkConsentAccepted ||
            !config.PartyPresenceSyncEnabled ||
            !config.PartyPresenceMarkersEnabled)
        {
            return;
        }

        uint territoryId = Plugin.ClientState.TerritoryType;
        var presences = PartyPresenceService.GetCurrentTerritorySnapshot(territoryId);
        uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.9f));

        foreach (var presence in presences)
        {
            Vector2 point = ToCanvas(presence.Position.ToVector3(), transform, canvasPos, canvasSize);

            if (!IsInsideCanvas(point, canvasPos, canvasSize))
                continue;

            uint color = ImGui.ColorConvertFloat4ToU32(presence.DisplayColor.ToVector4());

            drawList.AddCircleFilled(point, 5.5f, outlineColor);
            drawList.AddCircleFilled(point, 4.0f, color);
        }
    }

    private Vector2 ToCanvas(
        Vector3 world,
        MapTransformSnapshot transform,
        Vector2 canvasPos,
        Vector2 canvasSize)
    {
        return MapNormalizedToCanvas(
            MapCoordinateConverter.WorldToMapNormalized(world, transform),
            canvasPos,
            canvasSize
        );
    }

    private Vector2 MapNormalizedToCanvas(Vector2 normalized, Vector2 canvasPos, Vector2 canvasSize)
    {
        Vector2 local = (normalized - new Vector2(0.5f)) * BaseMapDisplaySize * this.zoom;

        return canvasPos + canvasSize * 0.5f + local + this.pan;
    }

    private static bool IsInsideCanvas(Vector2 point, Vector2 canvasPos, Vector2 canvasSize)
    {
        return point.X >= canvasPos.X &&
               point.X <= canvasPos.X + canvasSize.X &&
               point.Y >= canvasPos.Y &&
               point.Y <= canvasPos.Y + canvasSize.Y;
    }

    private static bool IsSegmentVisible(Vector2 a, Vector2 b, Vector2 canvasPos, Vector2 canvasSize)
    {
        if (IsInsideCanvas(a, canvasPos, canvasSize) || IsInsideCanvas(b, canvasPos, canvasSize))
            return true;

        float left = canvasPos.X;
        float right = canvasPos.X + canvasSize.X;
        float top = canvasPos.Y;
        float bottom = canvasPos.Y + canvasSize.Y;

        return MathF.Max(a.X, b.X) >= left &&
               MathF.Min(a.X, b.X) <= right &&
               MathF.Max(a.Y, b.Y) >= top &&
               MathF.Min(a.Y, b.Y) <= bottom;
    }

    private static string GetTerritoryLabel(uint territoryId)
    {
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();

        if (territorySheet != null &&
            territorySheet.TryGetRow(territoryId, out var territory))
        {
            string name = territory.PlaceName.Value.Name.ToString();

            if (!string.IsNullOrWhiteSpace(name))
                return $"{name} ({territoryId})";
        }

        return $"Territory {territoryId}";
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

    private Dictionary<string, int> GetDensityLookup(
    List<NavNodeSnapshot> nodes,
    float radius)
    {
        int signature = BuildDensitySignature(nodes, radius);

        if (signature == this.cachedDensitySignature)
            return this.cachedDensityByNodeId;

        this.cachedDensitySignature = signature;
        this.cachedDensityByNodeId = BuildDensityLookup(nodes, radius);
        return this.cachedDensityByNodeId;
    }

    private static int BuildDensitySignature(List<NavNodeSnapshot> nodes, float radius)
    {
        HashCode hash = new();

        hash.Add(nodes.Count);
        hash.Add(radius);

        foreach (var node in nodes)
        {
            hash.Add(node.Id);
            hash.Add(node.TraversalMode);
            hash.Add(node.Position);
            hash.Add(node.Links.Count);
        }

        return hash.ToHashCode();
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

    private void ApplyMouseWheelZoom(Vector2 canvasPos, Vector2 canvasSize)
    {
        float wheel = ImGui.GetIO().MouseWheel;

        if (MathF.Abs(wheel) < 0.001f)
            return;

        float oldZoom = this.zoom;
        float newZoom = Math.Clamp(oldZoom * MathF.Pow(1.12f, wheel), 0.1f, 20.0f);

        if (MathF.Abs(newZoom - oldZoom) < 0.001f)
            return;

        Vector2 mouse = ImGui.GetIO().MousePos;
        Vector2 center = canvasPos + canvasSize * 0.5f;
        Vector2 mapOffsetBeforeZoom = (mouse - center - this.pan) / oldZoom;

        this.zoom = newZoom;
        this.pan = mouse - center - mapOffsetBeforeZoom * newZoom;
    }

    private void CenterOnPlayer(MapTransformSnapshot transform, Vector2 canvasSize)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return;

        Vector2 normalized = MapCoordinateConverter.WorldToMapNormalized(player.Position, transform);
        this.pan = -(normalized - new Vector2(0.5f)) * BaseMapDisplaySize * this.zoom;
    }

    private void FitGraphToCanvas(
        List<NavNodeSnapshot> visibleNodes,
        MapTransformSnapshot transform,
        Vector2 canvasSize)
    {
        if (visibleNodes.Count == 0)
            return;

        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);

        foreach (var node in visibleNodes)
        {
            Vector2 normalized = MapCoordinateConverter.WorldToMapNormalized(node.Position, transform);
            min = Vector2.Min(min, normalized);
            max = Vector2.Max(max, normalized);
        }

        Vector2 span = Vector2.Max(max - min, new Vector2(0.02f));
        float zoomX = canvasSize.X * 0.85f / (BaseMapDisplaySize * MathF.Max(span.X, 0.02f));
        float zoomY = canvasSize.Y * 0.85f / (BaseMapDisplaySize * MathF.Max(span.Y, 0.02f));

        this.zoom = Math.Clamp(MathF.Min(zoomX, zoomY), 0.1f, 20.0f);

        Vector2 graphCenter = (min + max) * 0.5f;
        this.pan = -(graphCenter - new Vector2(0.5f)) * BaseMapDisplaySize * this.zoom;
    }

    private IDalamudTextureWrap? GetCurrentMapTexture(MapTransformSnapshot transform)
    {
        string path = transform.TexturePath;

        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (this.mapTexture != null && this.loadedTexturePath == path)
            return this.mapTexture;

        if (this.failedTexturePath == path)
            return null;

        this.mapTexture?.Dispose();
        this.mapTexture = null;
        this.loadedTexturePath = path;

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
                    this.failedTexturePath = string.Empty;
                    return this.mapTexture;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, $"Failed to load map texture candidate: {texturePath}");
            }
        }

        this.loadedTexturePath = string.Empty;
        this.failedTexturePath = path;
        return null;
    }
}
