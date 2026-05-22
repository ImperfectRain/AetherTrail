using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherTrail.Windows;

public sealed class NetworkConsentWindow : Window
{
    private readonly Plugin plugin;
    private Action? pendingEnableAction;

    public NetworkConsentWindow(Plugin plugin)
        : base("AetherTrail Network Consent###AetherTrailNetworkConsent")
    {
        this.plugin = plugin;
        this.Size = new Vector2(470, 245);
        this.SizeCondition = ImGuiCond.Always;
        this.Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
    }

    public void Prompt(Action enableAction)
    {
        this.pendingEnableAction = enableAction;
        this.IsOpen = true;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("AetherTrail network features are optional.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Graph sync uploads and downloads navigation graph data for the shared room code. " +
            "Party presence is a separate opt-in feature that shares your current territory, position, rotation, anonymous sync id, and chosen marker color while it is enabled."
        );
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Local recording, map rendering, importing, exporting, and navigation do not require network sync."
        );
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Accept and Enable", new Vector2(155, 0)))
        {
            this.plugin.Configuration.NetworkConsentAccepted = true;
            this.plugin.Configuration.Save();

            var enableAction = this.pendingEnableAction;
            this.pendingEnableAction = null;
            this.IsOpen = false;
            enableAction?.Invoke();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(100, 0)))
        {
            this.pendingEnableAction = null;
            this.IsOpen = false;
        }
    }
}
