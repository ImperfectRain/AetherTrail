using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AetherTrail;

public class OverlayRenderer
{
    public void Draw(bool trailEnabled, bool recordingEnabled, bool graphDebugEnabled)
    {
        if (!Plugin.Instance.Configuration.OverlayEnabled)
            return;

        uint territoryId = Plugin.ClientState.TerritoryType;
        int nodes = NavigationManager.GetNodeCount(territoryId);
        int links = NavigationManager.GetLinkCount(territoryId);

        ImGui.SetNextWindowBgAlpha(0.65f);
        ImGui.SetNextWindowPos(new Vector2(20f, 220f), ImGuiCond.FirstUseEver);

        ImGui.Begin(
            "AetherTrail Overlay",
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav
        );

        ImGui.Text("AetherTrail");
        ImGui.Separator();

        ImGui.Text($"Trail: {(trailEnabled ? "ON" : "OFF")}");
        ImGui.Text($"Recording: {(recordingEnabled ? "ON" : "OFF")}");
        ImGui.Text($"Graph Debug: {(graphDebugEnabled ? "ON" : "OFF")}");
        ImGui.Text($"Territory: {territoryId}");
        ImGui.Text($"Nodes: {nodes}");
        ImGui.Text($"Links: {links}");

        if (TargetResolver.TryGetTarget(out var target))
            ImGui.Text($"Target: {target.Label}");
        else
            ImGui.Text("Target: None");

        ImGui.End();
    }
}
