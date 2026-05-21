using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherTrail;

public sealed class PartyPresenceWorldRenderer
{
    public bool Enabled = true;

    public void Draw()
    {
        if (!this.Enabled)
            return;

        if (!Plugin.Instance.Configuration.PartyPresenceMarkersEnabled)
            return;

        uint territoryId = Plugin.ClientState.TerritoryType;

        var presences = PartyPresenceService.GetCurrentTerritorySnapshot(territoryId);

        if (presences.Count == 0)
            return;

        var drawList = ImGui.GetBackgroundDrawList();

        foreach (var presence in presences)
        {
            DrawPresenceMarker(drawList, presence);
        }
    }

    private static void DrawPresenceMarker(ImDrawListPtr drawList, PartySyncPresence presence)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        Vector3 position = presence.Position.ToVector3();

        if (player != null)
        {
            float distance = Vector3.Distance(player.Position, position);

            if (distance > Plugin.Instance.Configuration.PartyPresenceMaxDrawDistance)
                return;
        }

        Vector3 markerWorld = position + new Vector3(0f, 2.35f, 0f);

        if (!Plugin.GameGui.WorldToScreen(markerWorld, out Vector2 screenCenter))
            return;

        float ageSeconds = (float)(DateTime.UtcNow - presence.UpdatedAtUtc).TotalSeconds;
        float alpha = Math.Clamp(1f - (ageSeconds / 45f), 0.25f, 1f);

        DrawWireframeBeacon(
            drawList,
            position,
            presence.RotationRadians,
            alpha
        );

        DrawLabel(
            drawList,
            screenCenter + new Vector2(16f, -26f),
            presence.DisplayName,
            ageSeconds,
            alpha
        );
    }

    private static void DrawWireframeBeacon(
        ImDrawListPtr drawList,
        Vector3 basePosition,
        float rotationRadians,
        float alpha)
    {
        Vector3 center = basePosition + new Vector3(0f, 2.15f, 0f);

        float height = 1.15f;
        float radius = 0.55f;

        float pulse = 1.0f + MathF.Sin((float)DateTime.UtcNow.TimeOfDay.TotalSeconds * 4.0f) * 0.08f;

        height *= pulse;
        radius *= pulse;

        Vector3 top = center + new Vector3(0f, height, 0f);
        Vector3 bottom = center - new Vector3(0f, height * 0.65f, 0f);

        Vector3 forward = new(
            MathF.Sin(rotationRadians),
            0f,
            MathF.Cos(rotationRadians)
        );

        if (forward.LengthSquared() < 0.001f)
            forward = Vector3.UnitZ;

        forward = Vector3.Normalize(forward);

        Vector3 right = new(forward.Z, 0f, -forward.X);

        Vector3 front = center + forward * radius;
        Vector3 back = center - forward * radius;
        Vector3 left = center - right * radius;
        Vector3 rightPoint = center + right * radius;

        uint mainColor = ImGui.ColorConvertFloat4ToU32(
            new Vector4(1.0f, 0.1f, 0.85f, 0.95f * alpha)
        );

        uint shadowColor = ImGui.ColorConvertFloat4ToU32(
            new Vector4(0f, 0f, 0f, 0.75f * alpha)
        );

        const float shadowThickness = 3.5f;
        const float lineThickness = 1.8f;

        DrawBeaconEdges(drawList, top, bottom, front, rightPoint, back, left, shadowColor, shadowThickness);
        DrawBeaconEdges(drawList, top, bottom, front, rightPoint, back, left, mainColor, lineThickness);

        Vector3 nose = center + forward * (radius * 1.65f);

        DrawWorldLine(drawList, front, nose, shadowColor, shadowThickness);
        DrawWorldLine(drawList, front, nose, mainColor, lineThickness);
    }

    private static void DrawBeaconEdges(
        ImDrawListPtr drawList,
        Vector3 top,
        Vector3 bottom,
        Vector3 front,
        Vector3 rightPoint,
        Vector3 back,
        Vector3 left,
        uint color,
        float thickness)
    {
        DrawWorldLine(drawList, top, front, color, thickness);
        DrawWorldLine(drawList, top, rightPoint, color, thickness);
        DrawWorldLine(drawList, top, back, color, thickness);
        DrawWorldLine(drawList, top, left, color, thickness);

        DrawWorldLine(drawList, bottom, front, color, thickness);
        DrawWorldLine(drawList, bottom, rightPoint, color, thickness);
        DrawWorldLine(drawList, bottom, back, color, thickness);
        DrawWorldLine(drawList, bottom, left, color, thickness);

        DrawWorldLine(drawList, front, rightPoint, color, thickness);
        DrawWorldLine(drawList, rightPoint, back, color, thickness);
        DrawWorldLine(drawList, back, left, color, thickness);
        DrawWorldLine(drawList, left, front, color, thickness);
    }

    private static void DrawWorldLine(
        ImDrawListPtr drawList,
        Vector3 worldA,
        Vector3 worldB,
        uint color,
        float thickness)
    {
        if (!Plugin.GameGui.WorldToScreen(worldA, out Vector2 screenA))
            return;

        if (!Plugin.GameGui.WorldToScreen(worldB, out Vector2 screenB))
            return;

        if (Plugin.Instance.Configuration.HideTrailBehindUi &&
            (NativeUiOcclusionService.Contains(screenA) ||
             NativeUiOcclusionService.Contains(screenB)))
        {
            return;
        }

        drawList.AddLine(screenA, screenB, color, thickness);
    }

    private static void DrawLabel(
        ImDrawListPtr drawList,
        Vector2 position,
        string label,
        float ageSeconds,
        float alpha)
    {
        if (string.IsNullOrWhiteSpace(label))
            label = "Party";

        string text = $"{label} ({MathF.Round(ageSeconds)}s)";

        uint textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f * alpha));
        uint shadowColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.9f * alpha));

        drawList.AddText(position + new Vector2(1f, 1f), shadowColor, text);
        drawList.AddText(position, textColor, text);
    }
}
