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

        Vector2 screenForward = GetScreenForward(position, presence.RotationRadians, screenCenter);

        DrawArrow(drawList, screenCenter, screenForward, alpha);
        DrawLabel(drawList, screenCenter + new Vector2(16f, -26f), presence.DisplayName, ageSeconds, alpha);
    }

    private static Vector2 GetScreenForward(Vector3 position, float rotationRadians, Vector2 screenCenter)
    {
        Vector3 worldForward = new(
            MathF.Sin(rotationRadians),
            0f,
            MathF.Cos(rotationRadians)
        );

        Vector3 forwardWorld = position + worldForward * 1.75f + new Vector3(0f, 2.35f, 0f);

        if (!Plugin.GameGui.WorldToScreen(forwardWorld, out Vector2 forwardScreen))
            return new Vector2(0f, -1f);

        Vector2 direction = forwardScreen - screenCenter;

        if (direction.LengthSquared() < 0.001f)
            return new Vector2(0f, -1f);

        return Vector2.Normalize(direction);
    }

    private static void DrawArrow(
        ImDrawListPtr drawList,
        Vector2 center,
        Vector2 forward,
        float alpha)
    {
        float size = 18f;

        Vector2 right = new(forward.Y, -forward.X);

        Vector2 tip = center + forward * size;
        Vector2 left = center - forward * (size * 0.7f) - right * (size * 0.6f);
        Vector2 rightPoint = center - forward * (size * 0.7f) + right * (size * 0.6f);

        uint outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.9f * alpha));
        uint fillColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.1f, 0.85f, 0.95f * alpha));

        drawList.AddTriangleFilled(tip, left, rightPoint, fillColor);
        drawList.AddTriangle(tip, left, rightPoint, outlineColor, 2.5f);
        drawList.AddCircleFilled(center, 3.5f, outlineColor);
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
